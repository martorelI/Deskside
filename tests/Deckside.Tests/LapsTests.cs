using System.Globalization;
using Deckside.Core.Directory;
using Deckside.Core.Laps;
using Deckside.Directory;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public class LapsTests
{
    private static readonly DateTimeOffset Updated = new(2026, 11, 20, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = new(2026, 12, 20, 8, 30, 0, TimeSpan.Zero);

    private static string Json(string password) =>
        "{\"n\":\"Administrator\",\"t\":\"" + Updated.ToFileTime().ToString("x", CultureInfo.InvariantCulture) + "\",\"p\":\"" + password + "\"}";

    private static string Ft(DateTimeOffset d) => d.ToFileTime().ToString(CultureInfo.InvariantCulture);

    private static DirectoryObject Computer(params (string Name, string Value)[] attributes) =>
        TestDomain.Obj("CN=PC01," + TestDomain.ComputersOu,
            [("objectClass", ["computer"]), ("sAMAccountName", ["PC01$"]), .. attributes.Select(a => (a.Name, (IReadOnlyList<string>)[a.Value]))]);

    /// <summary>An encrypted value as AD stores it: 16-byte header + the DPAPI-NG buffer (here a fake one).</summary>
    private static string EncryptedValue(byte[] buffer)
    {
        var blob = new byte[16 + buffer.Length];
        BitConverter.GetBytes((uint)buffer.Length).CopyTo(blob, 8);
        buffer.CopyTo(blob, 16);
        return Convert.ToBase64String(blob);
    }

    [Fact]
    public void Windows_laps_in_clear_text()
    {
        var r = LapsReader.Read(Computer((LapsReader.Password, Json("S3cret!x")), (LapsReader.Expiration, Ft(Expires))), null);
        Assert.Equal("OK", r.Status);
        Assert.Equal(LapsSources.WindowsLaps, r.Source);
        Assert.Equal("Administrator", r.Account);
        Assert.Equal("S3cret!x", r.Password);
        Assert.Equal(Updated, r.UpdatedUtc);
        Assert.Equal(Expires, r.ExpiresUtc);
    }

    [Fact]
    public void Encrypted_is_preferred_and_only_the_buffer_after_the_header_is_decrypted()
    {
        byte[] fake = [1, 2, 3, 4, 5];
        byte[]? received = null;
        var r = LapsReader.Read(
            Computer((LapsReader.EncryptedPassword, EncryptedValue(fake)), (LapsReader.Password, Json("old")), (LapsReader.Expiration, Ft(Expires))),
            b => { received = b; return Json("FromDpapiNg") + "\0"; });
        Assert.Equal(fake, received);
        Assert.Equal(LapsSources.WindowsLapsEncrypted, r.Source);
        Assert.Equal("FromDpapiNg", r.Password);
    }

    [Fact]
    public void Decrypt_failure_is_reported_without_falling_back_to_legacy()
    {
        var r = LapsReader.Read(
            Computer((LapsReader.EncryptedPassword, EncryptedValue([9, 9])), (LapsReader.LegacyPassword, "legacy")),
            _ => throw new System.ComponentModel.Win32Exception(-2146893802));
        Assert.Equal("DECRYPT_FAILED", r.Status);
        Assert.Equal("", r.Password);
        var noDecryptor = LapsReader.Read(Computer((LapsReader.EncryptedPassword, EncryptedValue([9]))), null);
        Assert.Equal("DECRYPT_FAILED", noDecryptor.Status);
        Assert.Equal("NOT_SUPPORTED", noDecryptor.Detail);
    }

    [Fact]
    public void Legacy_laps_and_nothing()
    {
        var legacy = LapsReader.Read(Computer((LapsReader.LegacyPassword, "L3gacy"), (LapsReader.LegacyExpiration, Ft(Expires))), null);
        Assert.Equal(LapsSources.LegacyLaps, legacy.Source);
        Assert.Equal("L3gacy", legacy.Password);
        Assert.Equal("", legacy.Account);
        Assert.Equal(Expires, legacy.ExpiresUtc);
        Assert.Equal("NO_PASSWORD", LapsReader.Read(Computer(), null).Status);
        Assert.Equal("INVALID_DATA", LapsReader.Read(Computer((LapsReader.Password, "{not json")), null).Status);
    }

    [Fact]
    public void Invalid_encrypted_header()
    {
        Assert.Throws<FormatException>(() => LapsReader.EncryptedBuffer(new byte[10]));
        var badSize = new byte[20];
        BitConverter.GetBytes(999u).CopyTo(badSize, 8);
        Assert.Throws<FormatException>(() => LapsReader.EncryptedBuffer(badSize));
    }

    [Theory]
    [InlineData("pc01", "PC01")]
    [InlineData(" PC01$ ", "PC01")]
    [InlineData("pc-01.example.local", "PC-01")]
    [InlineData("bad name", "")]
    [InlineData("", "")]
    [InlineData("ABCDEFGHIJKLMNOP", "")]
    public void Computer_name(string typed, string expected) => Assert.Equal(expected, LapsReader.NormalizeComputerName(typed));

    [Fact]
    public void Encrypted_blob_is_kept_as_base64()
    {
        Assert.Equal("AQID", BinaryValues.ToText("msLAPS-EncryptedPassword", [1, 2, 3]));
        Assert.True(BinaryValues.IsBinary("MSLAPS-ENCRYPTEDPASSWORD"));
    }
}
