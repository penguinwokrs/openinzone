using System.Collections.Concurrent;
using InzoneBuds.Hid;
using InzoneBuds.Native;

namespace InzoneBuds.Protocol;

public sealed class PacketReceivedEventArgs(HciPacket packet) : EventArgs
{
    public HciPacket Packet { get; } = packet;
}

/// <summary>
/// Runs the request/response conversation over <see cref="HidTransport"/>.
/// A background reader reassembles packets from the report stream, hands replies to whoever is waiting,
/// and raises <see cref="PacketReceived"/> for anything unsolicited — for example when the wearer
/// changes a setting from the earbuds themselves.
/// </summary>
public sealed class HciSession : IDisposable
{
    private readonly HidTransport _transport;
    private readonly Thread _reader;
    private readonly IntPtr _cancelEvent;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ConcurrentDictionary<(EventId, ushort), TaskCompletionSource<HciPacket>> _pending = new();
    private readonly List<byte> _rxBuffer = [];
    private ushort _nextTransactionId = 1;
    private volatile bool _disposed;

    /// <summary>
    /// Which endpoint owns each setting. Link state lives on the dongle; everything else lives on the earbuds.
    /// </summary>
    private static readonly HashSet<EventId> TransmitterOwned =
    [
        EventId.ConnectStatus2Ghz,
        EventId.BootStatus,
    ];

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;

    public HciSession(HidTransport transport)
    {
        _transport = transport;
        _cancelEvent = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "inzone-hci-reader" };
        _reader.Start();
    }

    public static DeviceAddress DestinationFor(EventId eventId) =>
        TransmitterOwned.Contains(eventId) ? DeviceAddress.Transmitter : DeviceAddress.Receiver;

    /// <summary>Reads a value. Returns the parameter bytes the device replied with.</summary>
    public byte[] Get(EventId eventId, int timeoutMilliseconds = 1500)
        => Request(eventId, EventType.Get, [], timeoutMilliseconds).Param;

    /// <summary>Writes a value and waits for the device to acknowledge it.</summary>
    public byte[] Set(EventId eventId, ReadOnlySpan<byte> param, int timeoutMilliseconds = 1500)
        => Request(eventId, EventType.Set, param, timeoutMilliseconds).Param;

    public HciPacket Request(EventId eventId, EventType eventType, ReadOnlySpan<byte> param, int timeoutMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _requestGate.Wait();
        try
        {
            ushort transactionId = _nextTransactionId++;
            if (_nextTransactionId == 0) _nextTransactionId = 1;

            var completion = new TaskCompletionSource<HciPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
            var key = (eventId, transactionId);
            _pending[key] = completion;

            try
            {
                // Parameters longer than one packet are split; the device reassembles them.
                int offset = 0;
                do
                {
                    int chunk = Math.Min(param.Length - offset, HciPacket.MaxParamPerPacket);
                    var packet = HciPacket.CreateCommand(eventId, eventType, DestinationFor(eventId),
                        transactionId, param.Slice(offset, chunk));
                    _transport.Write(packet.ToArray());
                    offset += chunk;
                }
                while (offset < param.Length);

                if (!completion.Task.Wait(timeoutMilliseconds))
                    throw new TimeoutException(
                        $"The headset did not answer {eventType} {eventId} within {timeoutMilliseconds} ms. " +
                        "It may be powered off or out of range.");

                return completion.Task.Result;
            }
            finally
            {
                _pending.TryRemove(key, out _);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void ReadLoop()
    {
        while (!_disposed)
        {
            byte[]? payload;
            try
            {
                payload = _transport.Read(500, _cancelEvent);
            }
            catch (Exception) when (_disposed)
            {
                return;
            }
            catch (IOException)
            {
                return; // device unplugged
            }

            if (payload is null || payload.Length == 0) continue;

            _rxBuffer.AddRange(payload);
            DrainBuffer();
        }
    }

    private void DrainBuffer()
    {
        while (_rxBuffer.Count > 0)
        {
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_rxBuffer);
            int? total = HciPacket.PeekLength(span);

            if (total is null) return;             // need more bytes before the length is known
            if (total < 0)                          // not a packet start: drop a byte and resynchronise
            {
                _rxBuffer.RemoveAt(0);
                continue;
            }
            if (_rxBuffer.Count < total.Value) return;

            var packet = HciPacket.Parse(span[..total.Value]);
            _rxBuffer.RemoveRange(0, total.Value);
            if (packet is not null) Dispatch(packet);
        }
    }

    private void Dispatch(HciPacket packet)
    {
        if (_pending.TryGetValue((packet.EventId, packet.TransactionId), out var completion)
            && (packet.EventType.HasFlag(EventType.Ret) || packet.EventType.HasFlag(EventType.Notify)))
        {
            completion.TrySetResult(packet);
            return;
        }

        try { PacketReceived?.Invoke(this, new PacketReceivedEventArgs(packet)); }
        catch { /* a misbehaving subscriber must not kill the reader */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        NativeMethods.SetEvent(_cancelEvent);
        _reader.Join(1000);
        NativeMethods.CloseHandle(_cancelEvent);
        _requestGate.Dispose();
    }
}
