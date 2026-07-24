using System.Globalization;
using R3;

namespace TouchChanX.Win32.Battery;

/// <summary>
/// Display-ready snapshot for the Compact battery HUD.
/// </summary>
public readonly record struct BatteryHudSnapshot(
    string StatusText,
    string PercentText,
    string TimeLeftText,
    string PowerDrawText,
    string CapacityText,
    double PercentFraction,
    bool HasBattery,
    bool IsCharging);

/// <summary>
/// Battery monitoring business logic ported from TachiChan FunctionPage:
/// discharge averaging, capacity interpolation, AC / recovery handling.
/// </summary>
public sealed class BatteryMonitor
{
    private const string OnBatteryLabel = "On battery";
    private const string ChargingLabel = "Charging";
    private const string RecoveringLabel = "Recovering";

    // mWh capacity reported by the last successful sample.
    private int _lastCapacity;
    // Number of consecutive seconds for which the raw discharge rate stayed unchanged.
    private int _lastDischargeRate;
    private int _stableRateSeconds;
    private double _displayCapacityMwh;
    private long _averageRateMw;
    private long _totalEnergyMwSeconds;
    private int _totalSeconds;
    private int _reserveCapacityMwh;
    private bool _wasOnAc;
    private int _recoveringSeconds;

    public static bool IsAvailable() => BatteryQuery.IsAvailable();

    public void Reset()
    {
        _lastCapacity = 0;
        _lastDischargeRate = 0;
        _stableRateSeconds = 0;
        _displayCapacityMwh = 0;
        _averageRateMw = 0;
        _totalEnergyMwSeconds = 0;
        _totalSeconds = 0;
        _reserveCapacityMwh = 0;
        _wasOnAc = false;
        _recoveringSeconds = 0;
    }

    /// <summary>
    /// Advances the monitor by one second and returns a HUD snapshot.
    /// </summary>
    public BatteryHudSnapshot Tick()
    {
        if (BatteryQuery.IsAcPowerOnline())
        {
            _wasOnAc = true;
            ResetDischargeStats();
            return BuildFromRaw(tryRead: true, forceCharging: true);
        }

        if (_wasOnAc)
        {
            // Battery rate/capacity is unstable right after unplugging.
            _wasOnAc = false;
            _recoveringSeconds = 10;
            ResetDischargeStats();
        }

        if (_recoveringSeconds > 0)
        {
            _recoveringSeconds--;
            return BuildRecovering();
        }

        if (!BatteryQuery.TryGetInfo(out var info))
            return Unavailable;

        if (_reserveCapacityMwh <= 0 && info.FullChargeCapacityMwh > 0)
            _reserveCapacityMwh = info.FullChargeCapacityMwh * 6 / 100;

        _totalSeconds++;

        _stableRateSeconds = info.DischargeRateMw == _lastDischargeRate
            ? _stableRateSeconds + 1
            : 0;

        _displayCapacityMwh = info.CurrentCapacityMwh == _lastCapacity
            ? _displayCapacityMwh + info.DischargeRateMw / 3600.0
            : info.CurrentCapacityMwh;

        if (_totalEnergyMwSeconds == 0)
        {
            _averageRateMw = info.DischargeRateMw;
            _totalEnergyMwSeconds = -info.DischargeRateMw;
        }
        else
        {
            _totalEnergyMwSeconds -= info.DischargeRateMw;
            _averageRateMw = -_totalEnergyMwSeconds / Math.Max(1, _totalSeconds);
        }

        _lastDischargeRate = info.DischargeRateMw;
        _lastCapacity = info.CurrentCapacityMwh;

        return BuildDischarging(info);
    }

    /// <summary>
    /// Emits a snapshot immediately, then every second while subscribed.
    /// Must be observed on the UI time provider when driving UI updates.
    /// </summary>
    public Observable<BatteryHudSnapshot> Observe(TimeSpan? period = null)
    {
        var interval = period ?? TimeSpan.FromSeconds(1);
        return Observable.Create<BatteryHudSnapshot>(observer =>
        {
            Reset();
            observer.OnNext(Tick());
            return Observable.Interval(interval)
                .Subscribe(_ => observer.OnNext(Tick()));
        });
    }

    private void ResetDischargeStats()
    {
        _lastCapacity = 0;
        _lastDischargeRate = 0;
        _stableRateSeconds = 0;
        _displayCapacityMwh = 0;
        _averageRateMw = 0;
        _totalEnergyMwSeconds = 0;
        _totalSeconds = 0;
    }

    private BatteryHudSnapshot BuildRecovering()
    {
        if (!BatteryQuery.TryGetInfo(out var info) || info.FullChargeCapacityMwh <= 0)
            return new BatteryHudSnapshot(RecoveringLabel, "--", "--", "--", "--", 0, false, false);

        var fraction = Math.Clamp(info.CurrentCapacityMwh / (double)info.FullChargeCapacityMwh, 0, 1);
        return new BatteryHudSnapshot(
            RecoveringLabel,
            FormatPercent(fraction),
            "--",
            "--",
            FormatCapacityWh(info.CurrentCapacityMwh),
            fraction,
            true,
            false);
    }

    private BatteryHudSnapshot BuildFromRaw(bool tryRead, bool forceCharging)
    {
        if (forceCharging)
        {
            if (tryRead && BatteryQuery.TryGetInfo(out var info) && info.FullChargeCapacityMwh > 0)
            {
                var fraction = Math.Clamp(info.CurrentCapacityMwh / (double)info.FullChargeCapacityMwh, 0, 1);
                return new BatteryHudSnapshot(
                    ChargingLabel,
                    FormatPercent(fraction),
                    "--",
                    "--",
                    FormatCapacityWh(info.CurrentCapacityMwh),
                    fraction,
                    true,
                    true);
            }

            return new BatteryHudSnapshot(ChargingLabel, "--", "--", "--", "--", 0, false, true);
        }

        return Unavailable;
    }

    private BatteryHudSnapshot BuildDischarging(BatteryRawInfo info)
    {
        if (info.FullChargeCapacityMwh <= 0)
            return Unavailable;

        var fraction = Math.Clamp(info.CurrentCapacityMwh / (double)info.FullChargeCapacityMwh, 0, 1);

        var powerDrawText = _averageRateMw < 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{Math.Abs(_averageRateMw) / 1000.0:0.0} W ({_stableRateSeconds}s)")
            : "--";

        var timeLeftText = "--";
        if (_averageRateMw < 0 && _displayCapacityMwh > 0)
        {
            var totalSeconds = (long)(_displayCapacityMwh / -_averageRateMw * 3600.0);
            if (totalSeconds > 0)
            {
                var hours = totalSeconds / 3600;
                var minutes = totalSeconds % 3600 / 60;
                timeLeftText = hours > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m")
                    : string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
            }
        }

        return new BatteryHudSnapshot(
            OnBatteryLabel,
            FormatPercent(fraction),
            timeLeftText,
            powerDrawText,
            FormatCapacityWh(_displayCapacityMwh > 0 ? _displayCapacityMwh : info.CurrentCapacityMwh),
            fraction,
            true,
            false);
    }

    private static BatteryHudSnapshot Unavailable { get; } =
        new("--", "--", "--", "--", "--", 0, false, false);

    private static string FormatPercent(double fraction) =>
        fraction <= 0
            ? "--"
            : string.Create(CultureInfo.InvariantCulture, $"{fraction * 100:0}%");

    private static string FormatCapacityWh(double capacityMwh) =>
        capacityMwh > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{capacityMwh / 1000.0:0.0} Wh")
            : "--";
}
