using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TimeGuard.Parent;
using TimeGuard.Core;
using Xunit;
public class ProfileTests
{
    [Fact]
    public void Legacy_migrates_and_clients_and_password_survive_reload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TimeGuard-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var (hash,salt) = MasterPassword.Create("old-password");
            var a = new ParentConfig { ClientMachineId="a", ClientLabel="A", SecretBase64=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), MasterHashBase64=hash, MasterSaltBase64=salt };
            byte[] legacy = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(a,new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8.GetBytes("TimeGuardParent.v1"),DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(dir,"parent.dat"),legacy);
            var store = new ParentStore(dir);
            Assert.Equal("a",store.Load()!.ClientMachineId);
            Assert.Equal(legacy,File.ReadAllBytes(Path.Combine(dir,"parent.dat.single-client-backup")));
            var b = new ParentConfig { ClientMachineId="b",ClientLabel="B",SecretBase64=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),MasterHashBase64=hash,MasterSaltBase64=salt };
            store.Save(b); store.Save(a);
            (a.MasterHashBase64,a.MasterSaltBase64)=MasterPassword.Create("new-password"); store.Save(a);
            var reload=new ParentStore(dir);
            Assert.Equal("a",reload.Load()!.ClientMachineId); Assert.Equal(2,reload.Clients.Count);
            Assert.True(MasterPassword.Verify("new-password",reload.Load()!.MasterHashBase64,reload.Load()!.MasterSaltBase64));
            Assert.Equal(hash,reload.Clients.Single(c=>c.ClientMachineId=="b").MasterHashBase64);
            string file=Path.Combine(dir,"pair.json"); Pairing.Export(a,file);
            var imported=Pairing.Import(file)!; Assert.Equal(a.ClientMachineId,imported.ClientMachineId); Assert.Equal(a.SecretBase64,imported.SecretBase64); Assert.Equal(a.MasterHashBase64,imported.MasterHashBase64);
            reload.Save(imported); Assert.Equal(2,new ParentStore(dir).Clients.Count);
        }
        finally { Directory.Delete(dir,true); }
    }
}
