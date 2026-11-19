using System.Security.Cryptography;
using TimeGuard.Core;
using Xunit;

namespace TimeGuard.Core.Tests;

public class CodecTests
{
    private static byte[] Secret() => RandomNumberGenerator.GetBytes(32);
    private const long T0 = 1_700_000_000;

    [Fact]
    public void Code_verifies_on_client_with_same_secret_machine_and_time()
    {
        byte[] secret = Secret();
        const string machine = "CLIENT-PC-001";
        var parent = new TimeGuardCodec(secret, machine);
        var client = new TimeGuardCodec(secret, machine);

        string code = parent.Generate(minutes: 30, unixSeconds: T0);
        CodeVerifyResult r = client.Verify(code, T0);

        Assert.True(r.IsValid);
        Assert.Equal(30, r.Payload.Minutes);
    }

    [Fact]
    public void Code_is_nine_digits_grouped_by_three()
    {
        var codec = new TimeGuardCodec(Secret(), "M");
        string code = codec.Generate(30, T0);
        Assert.Matches(@"^\d{3}-\d{3}-\d{3}$", code);
    }

    [Fact]
    public void Code_is_rejected_on_a_different_machine()
    {
        byte[] secret = Secret();
        var parent = new TimeGuardCodec(secret, "CLIENT-A");
        var other = new TimeGuardCodec(secret, "CLIENT-B");
        Assert.Equal(CodeVerifyStatus.BadSignature, other.Verify(parent.Generate(30, T0), T0).Status);
    }

    [Fact]
    public void Code_is_rejected_with_a_different_secret()
    {
        const string machine = "CLIENT-A";
        var parent = new TimeGuardCodec(Secret(), machine);
        var client = new TimeGuardCodec(Secret(), machine);
        Assert.Equal(CodeVerifyStatus.BadSignature, client.Verify(parent.Generate(30, T0), T0).Status);
    }

    [Fact]
    public void Code_is_accepted_within_the_window_and_rejected_outside()
    {
        var codec = new TimeGuardCodec(Secret(), "CLIENT-A");
        string code = codec.Generate(15, T0);

        long within = T0 + TimeGuardCodec.WindowSteps * TimeGuardCodec.PeriodSeconds;
        Assert.True(codec.Verify(code, within).IsValid);

        long beyond = T0 + (TimeGuardCodec.WindowSteps + 1) * TimeGuardCodec.PeriodSeconds + 1;
        Assert.Equal(CodeVerifyStatus.BadSignature, codec.Verify(code, beyond).Status);
    }

    [Fact]
    public void Two_parents_with_same_secret_produce_independently_valid_codes()
    {
        byte[] secret = Secret();
        const string machine = "CLIENT-A";
        var a = new TimeGuardCodec(secret, machine);
        var b = new TimeGuardCodec(secret, machine);
        var client = new TimeGuardCodec(secret, machine);

        Assert.True(client.Verify(a.Generate(15, T0), T0).IsValid);
        Assert.True(client.Verify(b.Generate(30, T0), T0).IsValid);
    }

    [Fact]
    public void Input_with_or_without_dashes_and_spaces_is_accepted()
    {
        var codec = new TimeGuardCodec(Secret(), "M");
        string code = codec.Generate(20, T0);          // "XXX-XXX-XXX"
        string noDash = code.Replace("-", "");
        Assert.True(codec.Verify(noDash, T0).IsValid);
        Assert.True(codec.Verify(" " + code + " ", T0).IsValid);
    }

    [Theory]
    [InlineData("not-a-code")]
    [InlineData("")]
    [InlineData("123-456")]     // мало цифр
    [InlineData("1234567890")]  // много цифр
    public void Malformed_input_is_rejected(string bad)
    {
        var codec = new TimeGuardCodec(Secret(), "M");
        Assert.Equal(CodeVerifyStatus.MalformedCode, codec.Verify(bad, T0).Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(999)]
    public void Roundtrip_preserves_minutes(int minutes)
    {
        var codec = new TimeGuardCodec(Secret(), "M");
        var r = codec.Verify(codec.Generate(minutes, T0), T0);
        Assert.True(r.IsValid);
        Assert.Equal(minutes, r.Payload.Minutes);
    }

    [Fact]
    public void Backup_code_is_ten_digits_and_roundtrips()
    {
        var codec = new TimeGuardCodec(Secret(), "M");
        string code = codec.GenerateBackup(index: 0, minutes: 30);
        Assert.Matches(@"^\d{3}-\d{3}-\d{4}$", code);
        Assert.True(codec.VerifyBackup(code, out int mins, out string tag));
        Assert.Equal(30, mins);
        Assert.Equal(10, tag.Length);
    }

    [Fact]
    public void Backup_code_is_deterministic_across_parents()
    {
        byte[] secret = Secret();
        var a = new TimeGuardCodec(secret, "M");
        var b = new TimeGuardCodec(secret, "M");
        Assert.Equal(a.GenerateBackup(3, 60), b.GenerateBackup(3, 60));
    }

    [Fact]
    public void Backup_code_rejected_on_other_machine()
    {
        byte[] secret = Secret();
        var parent = new TimeGuardCodec(secret, "A");
        var other = new TimeGuardCodec(secret, "B");
        Assert.False(other.VerifyBackup(parent.GenerateBackup(1, 30), out _, out _));
    }

    [Fact]
    public void Live_and_backup_codes_do_not_collide()
    {
        var codec = new TimeGuardCodec(Secret(), "M");
        // 9-значный временный код не проходит как резервный, и наоборот
        Assert.False(codec.VerifyBackup(codec.Generate(30, T0), out _, out _));
    }
}

public class MasterPasswordTests
{
    [Fact]
    public void Correct_password_verifies()
    {
        var (hash, salt) = MasterPassword.Create("Sup3r-Secret!");
        Assert.True(MasterPassword.Verify("Sup3r-Secret!", hash, salt));
    }

    [Fact]
    public void Wrong_password_fails()
    {
        var (hash, salt) = MasterPassword.Create("Sup3r-Secret!");
        Assert.False(MasterPassword.Verify("wrong", hash, salt));
    }
}
