using System.IO;
using System.Text;
using System.Threading.Tasks;
using Moffat.EndlessOnline.SDK.Data;
using Moffat.EndlessOnline.SDK.Protocol.Pub;

namespace SOE_PubEditor.Services;

/// <summary>
/// Service for loading and saving pub files using eolib-dotnet SDK.
/// </summary>
public class PubFileService : IPubFileService
{
    private static readonly byte[] LfsHeader = Encoding.ASCII.GetBytes("version https://git-lfs");

    /// <summary>
    /// Returns true if the file on disk is a git-lfs pointer stub rather than real binary data.
    /// </summary>
    public static bool IsLfsPointer(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var buf = new byte[LfsHeader.Length];
            using var fs = File.OpenRead(filePath);
            int n = fs.Read(buf, 0, buf.Length);
            if (n < LfsHeader.Length) return false;
            for (int i = 0; i < LfsHeader.Length; i++)
                if (buf[i] != LfsHeader[i]) return false;
            return true;
        }
        catch { return false; }
    }

    public async Task<Eif> LoadItemsAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            if (IsLfsPointer(filePath))
                throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is a git-lfs pointer stub. Run 'git lfs pull' in the server repo to download the real file.");
            var bytes = File.ReadAllBytes(filePath);
            var reader = new EoReader(bytes);
            var eif = new Eif();
            eif.Deserialize(reader);
            return eif;
        });
    }

    public async Task<Enf> LoadNpcsAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            if (IsLfsPointer(filePath))
                throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is a git-lfs pointer stub. Run 'git lfs pull' in the server repo to download the real file.");
            var bytes = File.ReadAllBytes(filePath);
            var reader = new EoReader(bytes);
            var enf = new Enf();
            enf.Deserialize(reader);
            return enf;
        });
    }

    public async Task<Esf> LoadSpellsAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            if (IsLfsPointer(filePath))
                throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is a git-lfs pointer stub. Run 'git lfs pull' in the server repo to download the real file.");
            var bytes = File.ReadAllBytes(filePath);
            var reader = new EoReader(bytes);
            var esf = new Esf();
            esf.Deserialize(reader);
            return esf;
        });
    }

    public async Task<Ecf> LoadClassesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            if (IsLfsPointer(filePath))
                throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is a git-lfs pointer stub. Run 'git lfs pull' in the server repo to download the real file.");
            var bytes = File.ReadAllBytes(filePath);
            var reader = new EoReader(bytes);
            var ecf = new Ecf();
            ecf.Deserialize(reader);
            return ecf;
        });
    }

    public async Task SaveItemsAsync(string filePath, Eif data)
    {
        await Task.Run(() =>
        {
            var writer = new EoWriter();
            data.Serialize(writer);
            File.WriteAllBytes(filePath, writer.ToByteArray());
        });
    }

    public async Task SaveNpcsAsync(string filePath, Enf data)
    {
        await Task.Run(() =>
        {
            var writer = new EoWriter();
            data.Serialize(writer);
            File.WriteAllBytes(filePath, writer.ToByteArray());
        });
    }

    public async Task SaveSpellsAsync(string filePath, Esf data)
    {
        await Task.Run(() =>
        {
            var writer = new EoWriter();
            data.Serialize(writer);
            File.WriteAllBytes(filePath, writer.ToByteArray());
        });
    }

    public async Task SaveClassesAsync(string filePath, Ecf data)
    {
        await Task.Run(() =>
        {
            var writer = new EoWriter();
            data.Serialize(writer);
            File.WriteAllBytes(filePath, writer.ToByteArray());
        });
    }
}
