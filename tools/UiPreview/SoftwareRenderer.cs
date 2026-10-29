using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Dalamud.Bindings.ImGui;

internal sealed record Texture(int Width, int Height, byte[] Pixels);

internal static class SoftwareRenderer
{
    internal static unsafe void Render(ImDrawDataPtr data, IReadOnlyDictionary<ulong, Texture> textures, int width, int height, string path)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 5; pixels[i + 1] = 8; pixels[i + 2] = 13; pixels[i + 3] = 255; }
        for (var listIndex = 0; listIndex < data.CmdListsCount; listIndex++)
        {
            ImDrawListPtr list = data.CmdLists[listIndex];
            for (var commandIndex = 0; commandIndex < list.CmdBuffer.Size; commandIndex++)
            {
                var command = list.CmdBuffer[commandIndex];
                if (command.UserCallback != null) continue;
                if (!textures.TryGetValue(command.TextureId.Handle, out var texture)) throw new InvalidOperationException("Unknown preview texture.");
                for (var index = 0u; index < command.ElemCount; index += 3)
                {
                    var a = list.VtxBuffer[(int)(list.IdxBuffer[(int)(command.IdxOffset + index)] + command.VtxOffset)];
                    var b = list.VtxBuffer[(int)(list.IdxBuffer[(int)(command.IdxOffset + index + 1)] + command.VtxOffset)];
                    var c = list.VtxBuffer[(int)(list.IdxBuffer[(int)(command.IdxOffset + index + 2)] + command.VtxOffset)];
                    Triangle(pixels, width, height, a, b, c, command.ClipRect, texture);
                }
            }
        }
        WritePng(path, width, height, pixels);
    }

    // Evaluate reversed shared edges symmetrically so large image quads have no diagonal cracks.
    private static double Edge(Vector2 a, Vector2 b, Vector2 p) => ((double)b.Y - a.Y) * p.X +
        ((double)a.X - b.X) * p.Y + (double)a.Y * b.X - (double)a.X * b.Y;
    private static bool TopLeft(Vector2 a, Vector2 b) => a.Y < b.Y || (a.Y == b.Y && a.X > b.X);

    private static void Triangle(byte[] pixels, int width, int height, ImDrawVert a, ImDrawVert b, ImDrawVert c, Vector4 clip, Texture texture)
    {
        var area = Edge(a.Pos, b.Pos, c.Pos);
        if (Math.Abs(area) < 0.00001f) return;
        if (area < 0) { (b, c) = (c, b); area = -area; }
        var x0 = Math.Max(0, (int)MathF.Ceiling(MathF.Max(clip.X, MathF.Min(a.Pos.X, MathF.Min(b.Pos.X, c.Pos.X))) - 0.5f));
        var x1 = Math.Min(width - 1, (int)MathF.Floor(MathF.Min(clip.Z, MathF.Max(a.Pos.X, MathF.Max(b.Pos.X, c.Pos.X))) - 0.5f));
        var y0 = Math.Max(0, (int)MathF.Ceiling(MathF.Max(clip.Y, MathF.Min(a.Pos.Y, MathF.Min(b.Pos.Y, c.Pos.Y))) - 0.5f));
        var y1 = Math.Min(height - 1, (int)MathF.Floor(MathF.Min(clip.W, MathF.Max(a.Pos.Y, MathF.Max(b.Pos.Y, c.Pos.Y))) - 0.5f));
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            var e0 = Edge(b.Pos, c.Pos, p); var e1 = Edge(c.Pos, a.Pos, p); var e2 = Edge(a.Pos, b.Pos, p);
            if (e0 < 0 || e1 < 0 || e2 < 0 || (e0 == 0 && !TopLeft(b.Pos, c.Pos)) || (e1 == 0 && !TopLeft(c.Pos, a.Pos)) || (e2 == 0 && !TopLeft(a.Pos, b.Pos))) continue;
            var w0 = (float)(e0 / area); var w1 = (float)(e1 / area); var w2 = (float)(e2 / area);
            var uv = a.Uv * w0 + b.Uv * w1 + c.Uv * w2;
            var tx = Math.Clamp((int)(uv.X * texture.Width), 0, texture.Width - 1);
            var ty = Math.Clamp((int)(uv.Y * texture.Height), 0, texture.Height - 1);
            var texel = (ty * texture.Width + tx) * 4;
            var alpha = (((a.Col >> 24) & 255) * w0 + ((b.Col >> 24) & 255) * w1 + ((c.Col >> 24) & 255) * w2) * texture.Pixels[texel + 3] / (255f * 255f);
            if (alpha <= 0) continue;
            var target = (y * width + x) * 4;
            for (var channel = 0; channel < 3; channel++)
            {
                var shift = channel * 8;
                var color = (((a.Col >> shift) & 255) * w0 + ((b.Col >> shift) & 255) * w1 + ((c.Col >> shift) & 255) * w2) * texture.Pixels[texel + channel] / 255f;
                pixels[target + channel] = (byte)Math.Clamp(color * alpha + pixels[target + channel] * (1 - alpha), 0, 255);
            }
        }
    }

    private static void WritePng(string path, int width, int height, byte[] pixels)
    {
        using var output = File.Create(path);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (var y = 0; y < height; y++) { zlib.WriteByte(0); zlib.Write(pixels, y * width * 4, width * 4); }
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(integer, data.Length); stream.Write(integer);
        var name = System.Text.Encoding.ASCII.GetBytes(type); stream.Write(name); stream.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in name.Concat(data))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc); stream.Write(integer);
    }
}
