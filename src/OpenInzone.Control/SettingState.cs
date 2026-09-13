// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.Protocol;
using OpenInzone.Settings;

namespace OpenInzone.Control;

/// <summary>Keeps one complete settings reading current as headset notifications arrive.</summary>
internal sealed class SettingState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Change> _changes = [];
    private IReadOnlyList<SettingValue>? _current;
    private long _generation;

    private sealed record Change(SettingValue Value, long Generation);

    public long BeginRead()
    {
        lock (_gate) return _generation;
    }

    public IReadOnlyList<SettingValue> Replace(IReadOnlyList<SettingValue> settings) =>
        Replace(settings, BeginRead());

    public IReadOnlyList<SettingValue> Replace(
        IReadOnlyList<SettingValue> settings,
        long readGeneration)
    {
        lock (_gate)
        {
            return ReplaceLocked(settings, readGeneration);
        }
    }

    public void ReplaceAndPublish(
        IReadOnlyList<SettingValue> settings,
        long readGeneration,
        Action<IReadOnlyList<SettingValue>> publish)
    {
        lock (_gate) publish(ReplaceLocked(settings, readGeneration));
    }

    private SettingValue[] ReplaceLocked(
        IReadOnlyList<SettingValue> settings,
        long readGeneration)
    {
        SettingValue[] snapshot =
        [
            .. settings.Select(value =>
                _changes.TryGetValue(value.Id, out var change)
                && change.Generation > readGeneration
                    ? change.Value
                    : value),
        ];
        _current = snapshot;
        return snapshot;
    }

    public IReadOnlyList<SettingValue>? Apply(EventId eventId, byte[] param) =>
        Apply(eventId, param, null);

    public void ApplyAndPublish(
        EventId eventId,
        byte[] param,
        Action<IReadOnlyList<SettingValue>> publish) => Apply(eventId, param, publish);

    private IReadOnlyList<SettingValue>? Apply(
        EventId eventId,
        byte[] param,
        Action<IReadOnlyList<SettingValue>>? publish)
    {
        var descriptors = SettingCatalogue.ForEvent(eventId)
            .Where(setting => param.Length >= setting.PacketBytes)
            .ToArray();
        if (descriptors.Length == 0) return null;

        var replacements = descriptors.ToDictionary(
            setting => setting.Id,
            setting => new SettingValue(setting.Id, setting.Read(param)));

        lock (_gate)
        {
            long generation = ++_generation;
            foreach (var replacement in replacements)
                _changes[replacement.Key] = new Change(replacement.Value, generation);

            if (_current is null || !_current.Any(value => replacements.ContainsKey(value.Id)))
                return null;

            SettingValue[] next =
            [
                .. _current.Select(value =>
                    replacements.TryGetValue(value.Id, out var replacement) ? replacement : value),
            ];
            _current = next;
            publish?.Invoke(next);
            return next;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
            _changes.Clear();
        }
    }
}
