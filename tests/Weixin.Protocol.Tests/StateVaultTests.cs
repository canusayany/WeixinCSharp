using System.Text;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

public static class StateVaultTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "weixin-vault-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "state.bin");
        if (!OperatingSystem.IsWindows())
        {
            Throws<PlatformNotSupportedException>(() => { using var vault = new StateVault(path); });
            return;
        }

        try
        {
            const string secret = "fixture-secret-never-store-in-plaintext-4f180bdcb694";
            byte[] valid;
            using (var vault = new StateVault(path))
            {
                Assert(await vault.LoadAsync<TestState>() is null, "Missing state must return null.");
                await vault.SaveAsync(new TestState(secret, 7));
                Assert(await vault.LoadAsync<TestState>() == new TestState(secret, 7), "State must round-trip.");
                valid = await File.ReadAllBytesAsync(path);
                Assert(!Contains(valid, Encoding.UTF8.GetBytes(secret)), "Stored file must not contain the secret.");
                Assert(!Contains(valid, Encoding.UTF8.GetBytes("\"Counter\"")), "Stored JSON must be encrypted.");
                Throws<IOException>(() => { using var competingVault = new StateVault(path); });

                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await ThrowsAsync<OperationCanceledException>(() => vault.SaveAsync(new TestState("replacement", 8), cancellation.Token));
                Assert((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(valid), "Canceled save must preserve prior bytes.");
                Assert(await vault.LoadAsync<TestState>() == new TestState(secret, 7), "Canceled save must preserve prior state.");
                Assert(Directory.GetFiles(directory, "*.tmp").Length == 0, "Canceled save must leave no temporary file.");

                using var cancellationDuringSave = new CancellationTokenSource();
                await ThrowsAsync<OperationCanceledException>(() => vault.SaveAsync(
                    new CancellingState(cancellationDuringSave), cancellationDuringSave.Token));
                Assert((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(valid),
                    "Cancellation after save started must preserve prior bytes.");
                await ThrowsAsync<InvalidDataException>(() => vault.SaveAsync(new TestState(new string('a', 8 * 1024 * 1024), 9)));
                Assert((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(valid),
                    "Oversized save must preserve prior bytes.");

                byte[] damagedHeader = valid.ToArray();
                damagedHeader[0] ^= 0xff;
                await File.WriteAllBytesAsync(path, damagedHeader);
                await ThrowsAsync<InvalidDataException>(() => vault.LoadAsync<TestState>());

                byte[] damagedPayload = valid.ToArray();
                damagedPayload[^1] ^= 0xff;
                await File.WriteAllBytesAsync(path, damagedPayload);
                await ThrowsAsync<InvalidDataException>(() => vault.LoadAsync<TestState>());
                await File.WriteAllBytesAsync(path, new byte[1]);
                await ThrowsAsync<InvalidDataException>(() => vault.LoadAsync<TestState>());
                await File.WriteAllBytesAsync(path, valid);
            }

            // A released lifetime lock must permit a fresh vault to reopen persisted state.
            using (var reopened = new StateVault(path))
                Assert(await reopened.LoadAsync<TestState>() == new TestState(secret, 7), "Reopened state must round-trip.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public sealed record TestState(string Token, int Counter);

    private sealed class CancellingState(CancellationTokenSource cancellation)
    {
        public string Token
        {
            get { cancellation.Cancel(); return "replacement"; }
        }
    }
}
