using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CrackSaveEditor.Core;

/// <summary>
/// Mirrors the game's SaveFile class:
/// "CRK1" | IV(16) | AES-256-CBC/PKCS7 ciphertext | HMAC-SHA256(file-without-tag ++ int64 hostSteamId)
/// </summary>
public static class SaveCrypto
{
    const string KeyMaterial = "crack/save/v1/9f4c2e7a1b6d83f05e2a4c91d7b38e6f0a5c1d9e2b7f4a63|";
    const int IvLength = 16;
    const int TagLength = 32;

    static readonly byte[] Magic = "CRK1"u8.ToArray();
    static readonly byte[] EncKey = SHA256.HashData(Encoding.UTF8.GetBytes(KeyMaterial + "enc"));
    static readonly byte[] MacKey = SHA256.HashData(Encoding.UTF8.GetBytes(KeyMaterial + "mac"));

    public static bool IsEncrypted(byte[] data) =>
        data.Length >= Magic.Length && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static string Decrypt(byte[] file)
    {
        if (!IsEncrypted(file))
            throw new InvalidDataException(Loc.T("Not a CRACK save file (CRK1 header missing)."));
        if (file.Length < Magic.Length + IvLength + 16 + TagLength)
            throw new InvalidDataException(Loc.T("The file is truncated."));

        using var aes = Aes.Create();
        aes.Key = EncKey;
        int start = Magic.Length + IvLength;
        int end = file.Length - TagLength;
        byte[] plain = aes.DecryptCbc(file.AsSpan(start, end - start), file.AsSpan(Magic.Length, IvLength), PaddingMode.PKCS7);
        return Encoding.UTF8.GetString(plain);
    }

    public static byte[] Encrypt(string json, long hostId)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(IvLength);
        using var aes = Aes.Create();
        aes.Key = EncKey;
        byte[] cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(json), iv, PaddingMode.PKCS7);

        int bodyLen = Magic.Length + IvLength + cipher.Length;
        byte[] file = new byte[bodyLen + TagLength];
        Magic.CopyTo(file, 0);
        iv.CopyTo(file, Magic.Length);
        cipher.CopyTo(file, Magic.Length + IvLength);
        ComputeTag(file, bodyLen, hostId).CopyTo(file, bodyLen);
        return file;
    }

    public static byte[] ComputeTag(byte[] data, int length, long hostId)
    {
        using var hmac = new HMACSHA256(MacKey);
        hmac.TransformBlock(data, 0, length, null, 0);
        byte[] id = BitConverter.GetBytes(hostId);
        hmac.TransformFinalBlock(id, 0, id.Length);
        return hmac.Hash!;
    }

    public static bool VerifyTag(byte[] file, long hostId)
    {
        if (file.Length < TagLength) return false;
        int bodyLen = file.Length - TagLength;
        return CryptographicOperations.FixedTimeEquals(ComputeTag(file, bodyLen, hostId), file.AsSpan(bodyLen));
    }

    /// <summary>Returns the host SteamID whose signature matches the file, or null.</summary>
    public static long? FindHostId(byte[] file, IEnumerable<long> candidates)
    {
        foreach (long id in candidates.Distinct())
            if (VerifyTag(file, id)) return id;
        return null;
    }
}
