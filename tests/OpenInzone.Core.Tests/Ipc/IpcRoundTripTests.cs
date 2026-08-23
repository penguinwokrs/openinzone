// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

/// <summary>
/// Exercises a real named pipe rather than a stand-in. The framing, the reconnect loop and the
/// hello handshake are the parts most likely to be wrong, and none of them show up in a test that
/// substitutes the transport.
/// </summary>
public class IpcRoundTripTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static string UniquePipeName() => $"openinzone-test-{Guid.NewGuid():N}";

    private static readonly DeviceSnapshot Sample = new(
        true, "INZONE Buds", 16, 30, false, 40, false, 75, true,
        new BatterySnapshot(97, 94, null, true));

    private static async Task<T> Within<T>(TaskCompletionSource<T> completion) =>
        await completion.Task.WaitAsync(Patience);

    [Fact]
    public async Task A_client_is_told_the_current_state_as_soon_as_it_connects()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => Sample, pipeName);
        server.Start();

        var arrived = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, snapshot) => arrived.TrySetResult(snapshot);
        client.Start();

        Assert.Equal(Sample, await Within(arrived));
    }

    [Fact]
    public async Task A_command_reaches_the_server()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => Sample, pipeName);
        var received = new TaskCompletionSource<ClientMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CommandReceived += (_, message) => received.TrySetResult(message);
        server.Start();

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, _) => connected.TrySetResult(true);
        client.Start();
        await Within(connected);

        Assert.True(client.Send(IpcCommands.AdjustVolume, -2));

        var message = await Within(received);
        Assert.Equal(IpcCommands.AdjustVolume, message.Command);
        Assert.Equal(-2, message.Value);
    }

    [Fact]
    public async Task A_published_snapshot_reaches_a_connected_client()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => DeviceSnapshot.Disconnected, pipeName);
        server.Start();

        var snapshots = new List<DeviceSnapshot>();
        var pushed = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, snapshot) =>
        {
            lock (snapshots) snapshots.Add(snapshot);
            if (snapshot.Connected) pushed.TrySetResult(snapshot);
        };
        client.Start();

        while (server.ClientCount == 0) await Task.Delay(20);
        server.Publish(Sample);

        Assert.Equal(Sample, await Within(pushed));
        lock (snapshots) Assert.Equal(DeviceSnapshot.Disconnected, snapshots[0]);
    }

    [Fact]
    public async Task Asking_for_detail_brings_the_devices_own_answers_back()
    {
        string pipeName = UniquePipeName();
        var detail = new DeviceDetail("bW9kZWw=", "YmF0dA==", "YmFs", "dm9s", "bWlj", "c2lkZQ==", 75);

        using var server = new IpcServer(() => Sample, pipeName);
        server.CommandReceived += (sender, message) =>
        {
            if (message.Command == IpcCommands.Describe) ((IpcServer)sender!).Publish(detail);
        };
        server.Start();

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource<DeviceDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, _) => connected.TrySetResult(true);
        client.DetailReceived += (_, d) => arrived.TrySetResult(d);
        client.Start();
        await Within(connected);

        Assert.True(client.Send(IpcCommands.Describe));

        Assert.Equal(detail, await Within(arrived));
    }

    /// <summary>
    /// A request the daemon accepted but could not carry out - the headset went away mid-read -
    /// has to reach the caller, or a describe that failed looks like a channel gone quiet.
    /// </summary>
    [Fact]
    public async Task A_failure_to_carry_out_a_command_reaches_the_client()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => Sample, pipeName);
        server.Start();

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complaint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, _) => connected.TrySetResult(true);
        client.ServerError += (_, message) => complaint.TrySetResult(message);
        client.Start();
        await Within(connected);

        server.PublishError("ヘッドセットに接続されていません。");

        Assert.Equal("ヘッドセットに接続されていません。", await Within(complaint));
    }

    [Fact]
    public async Task An_unknown_command_is_reported_back_rather_than_ignored()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => Sample, pipeName);
        var seenByServer = false;
        server.CommandReceived += (_, _) => seenByServer = true;
        server.Start();

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complaint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, _) => connected.TrySetResult(true);
        client.ServerError += (_, message) => complaint.TrySetResult(message);
        client.Start();
        await Within(connected);

        Assert.True(client.Send("format-c"));

        Assert.Contains("format-c", await Within(complaint), StringComparison.Ordinal);
        Assert.False(seenByServer);
    }

    [Fact]
    public async Task The_link_going_down_is_reported_so_a_client_can_grey_itself_out()
    {
        string pipeName = UniquePipeName();
        var server = new IpcServer(() => Sample, pipeName);
        server.Start();

        var up = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var down = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.ConnectionChanged += (_, connected) =>
        {
            if (connected) up.TrySetResult(true); else down.TrySetResult(true);
        };
        client.Start();
        await Within(up);

        server.Dispose();

        Assert.True(await Within(down));
    }

    [Fact]
    public async Task A_client_that_starts_before_the_tray_connects_once_the_tray_appears()
    {
        string pipeName = UniquePipeName();

        var arrived = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, snapshot) => arrived.TrySetResult(snapshot);
        client.Start();

        // Long enough that the client has certainly failed to connect at least once.
        await Task.Delay(200);
        using var server = new IpcServer(() => Sample, pipeName);
        server.Start();

        Assert.Equal(Sample, await Within(arrived));
    }

    /// <summary>
    /// Snapshots have to arrive in the order they were published: a client that receives an older
    /// one last shows a stale reading until something else changes. A lock around the write would
    /// stop them interleaving without keeping them in order.
    /// </summary>
    [Fact]
    public async Task Snapshots_arrive_in_the_order_they_were_published()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => DeviceSnapshot.Disconnected, pipeName);
        server.Start();

        const int count = 40;
        var seen = new List<int>();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, snapshot) =>
        {
            if (!snapshot.Connected) return;   // the hello
            lock (seen)
            {
                seen.Add(snapshot.Volume);
                if (seen.Count == count) done.TrySetResult(true);
            }
        };
        client.Start();

        while (server.ClientCount == 0) await Task.Delay(20);
        for (int i = 0; i < count; i++)
            server.Publish(Sample with { Volume = i });

        await Within(done);
        lock (seen) Assert.Equal(Enumerable.Range(0, count), seen);
    }

    /// <summary>Commands are queued too: a turn then a press must not land the other way round.</summary>
    [Fact]
    public async Task Commands_reach_the_server_in_the_order_they_were_made()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => Sample, pipeName);

        const int count = 30;
        var seen = new List<int>();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CommandReceived += (_, message) =>
        {
            lock (seen)
            {
                seen.Add(message.Value);
                if (seen.Count == count) done.TrySetResult(true);
            }
        };
        server.Start();

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName);
        client.SnapshotReceived += (_, _) => connected.TrySetResult(true);
        client.Start();
        await Within(connected);

        for (int i = 0; i < count; i++) Assert.True(client.Send(IpcCommands.SetVolume, i));

        await Within(done);
        lock (seen) Assert.Equal(Enumerable.Range(0, count), seen);
    }

    /// <summary>
    /// The tray outlives many clients: a Stream Deck restart, a plugin reinstall, a deck being
    /// unplugged. Each one has to be served like the first.
    /// </summary>
    [Fact]
    public async Task Clients_that_come_and_go_are_each_served()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => Sample, pipeName);
        server.Start();

        for (int attempt = 0; attempt < 4; attempt++)
        {
            var arrived = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new IpcClient(pipeName);
            client.SnapshotReceived += (_, snapshot) => arrived.TrySetResult(snapshot);
            client.Start();

            Assert.Equal(Sample, await Within(arrived));
            client.Dispose();

            // The server has to notice the client has gone before the next one connects, or the
            // test proves only that several clients fit at once.
            while (server.ClientCount > 0) await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Several_clients_are_served_at_once()
    {
        string pipeName = UniquePipeName();
        using var server = new IpcServer(() => DeviceSnapshot.Disconnected, pipeName);
        server.Start();

        var clients = new List<IpcClient>();
        var pushes = new List<TaskCompletionSource<DeviceSnapshot>>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var pushed = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                var client = new IpcClient(pipeName);
                client.SnapshotReceived += (_, snapshot) => { if (snapshot.Connected) pushed.TrySetResult(snapshot); };
                client.Start();
                clients.Add(client);
                pushes.Add(pushed);
            }

            while (server.ClientCount < 3) await Task.Delay(20);
            server.Publish(Sample);

            foreach (var pushed in pushes) Assert.Equal(Sample, await Within(pushed));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }
}
