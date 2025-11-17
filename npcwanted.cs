using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

public class NpcWantedSystem : Script
{
    private const float MaxIncidentDistance = 250f;
    private const int MaxSpawnedCops = 30;
    private const int IntervalIdle = 5000;
    private const int IntervalActive = 2500;
    private const int IncidentTimeout = 45000;
    private const float BackupSpawnRadiusMin = 80f;
    private const float BackupSpawnRadiusMax = 140f;

    private NpcIncident _currentIncident;
    private readonly List<Ped> _spawnedCops = new List<Ped>();
    private readonly List<Vehicle> _spawnedVehicles = new List<Vehicle>();

    private readonly string[] _cityPoliceVehicleModels =
    {
        "police",
        "police2",
        "police3"
    };

    private readonly string[] _countyPoliceVehicleModels =
    {
        "sheriff",
        "sheriff2"
    };

    private readonly string[] _swatVehicleModels =
    {
        "fbi2"
    };

    private readonly Random _rng = new Random();

    public NpcWantedSystem()
    {
        Interval = IntervalIdle;
        Tick += OnTick;
        Aborted += OnAbort;
    }

    private void OnAbort(object sender, EventArgs e)
    {
        CleanupIncident();
    }

    private void OnTick(object sender, EventArgs e)
    {
        Ped player = Game.Player.Character;
        if (!player.Exists() || player.IsDead)
        {
            CleanupIncident();
            Interval = IntervalIdle;
            return;
        }

        if (_currentIncident == null)
        {
            LookForNewIncident(player);
        }
        else
        {
            UpdateCurrentIncident(player);
        }

        Interval = _currentIncident == null ? IntervalIdle : IntervalActive;
    }

    private void LookForNewIncident(Ped player)
    {
        Ped[] allPeds = World.GetAllPeds();

        Ped bestCop = null;
        Ped bestSuspect = null;
        float bestDistanceToPlayer = MaxIncidentDistance;

        foreach (Ped cop in allPeds)
        {
            if (!IsCop(cop))
                continue;

            if (!cop.IsInCombat || cop.IsDead)
                continue;

            foreach (Ped other in allPeds)
            {
                if (!IsPotentialSuspect(other, player))
                    continue;

                if (!other.IsInCombat)
                    continue;

                float distCopSuspect = cop.Position.DistanceTo(other.Position);
                if (distCopSuspect > 40f)
                    continue;

                float distToPlayer = player.Position.DistanceTo(other.Position);
                if (distToPlayer > MaxIncidentDistance)
                    continue;

                if (distToPlayer < bestDistanceToPlayer)
                {
                    bestDistanceToPlayer = distToPlayer;
                    bestCop = cop;
                    bestSuspect = other;
                }
            }
        }

        if (bestCop != null && bestSuspect != null)
        {
            Vector3 pos = bestSuspect.Position;
            _currentIncident = new NpcIncident(pos)
            {
                WantedLevel = 1,
                LastSeenTime = Game.GameTime,
                LastCombatTime = Game.GameTime
            };
            _currentIncident.Cops.Add(bestCop);
            _currentIncident.Suspects.Add(bestSuspect);
        }
    }

    private void UpdateCurrentIncident(Ped player)
    {
        if (_currentIncident == null)
            return;

        for (int i = _currentIncident.Cops.Count - 1; i >= 0; i--)
        {
            Ped cop = _currentIncident.Cops[i];
            if (cop == null || !cop.Exists())
            {
                _currentIncident.Cops.RemoveAt(i);
                continue;
            }

            if (cop.IsDead)
            {
                _currentIncident.CopsKilled++;
                _currentIncident.Cops.RemoveAt(i);
            }
        }

        _currentIncident.Suspects.RemoveAll(p => p == null || !p.Exists() || p.IsDead);

        if (_currentIncident.Suspects.Count == 0)
        {
            CleanupIncident();
            return;
        }

        Vector3 avg = Vector3.Zero;
        foreach (Ped s in _currentIncident.Suspects)
        {
            avg += s.Position;
        }
        avg /= _currentIncident.Suspects.Count;
        _currentIncident.Location = avg;

        float distanceToPlayer = player.Position.DistanceTo(_currentIncident.Location);
        if (distanceToPlayer > MaxIncidentDistance)
        {
            CleanupIncident();
            return;
        }

        bool anyCombat = false;
        foreach (Ped cop in _currentIncident.Cops)
        {
            if (cop.IsInCombat && !cop.IsDead)
            {
                anyCombat = true;
                break;
            }
        }
        if (!anyCombat)
        {
            foreach (Ped s in _currentIncident.Suspects)
            {
                if (s.IsInCombat && !s.IsDead)
                {
                    anyCombat = true;
                    break;
                }
            }
        }

        int now = Game.GameTime;
        if (anyCombat)
        {
            _currentIncident.LastCombatTime = now;
        }

        if (now - _currentIncident.LastCombatTime > IncidentTimeout)
        {
            CleanupIncident();
            return;
        }

        UpdateIncidentWantedLevel();
        SpawnBackupIfNecessary();
    }

    private void UpdateIncidentWantedLevel()
    {
        if (_currentIncident == null)
            return;

        int cops = _currentIncident.Cops.Count;
        int suspects = _currentIncident.Suspects.Count;
        int copsKilled = _currentIncident.CopsKilled;

        int wanted = 1;

        if (cops >= 2 || suspects >= 2)
            wanted = 2;

        if (cops >= 3 || suspects >= 3 || copsKilled >= 1)
            wanted = 3;

        if (cops >= 4 || copsKilled >= 2)
            wanted = 4;

        if (cops >= 6 || copsKilled >= 3)
            wanted = 5;

        wanted = Math.Max(1, Math.Min(5, wanted));
        _currentIncident.WantedLevel = wanted;
    }

    private void SpawnBackupIfNecessary()
    {
        if (_currentIncident == null)
            return;

        _spawnedCops.RemoveAll(p => p == null || !p.Exists() || p.IsDead);
        _spawnedVehicles.RemoveAll(v => v == null || !v.Exists());

        int totalCops = _currentIncident.Cops.Count + _spawnedCops.Count;

        int desiredCops = 2 + _currentIncident.WantedLevel * 2;
        desiredCops = Math.Min(desiredCops, MaxSpawnedCops);

        if (totalCops < desiredCops)
        {
            int toSpawn = desiredCops - totalCops;
            for (int i = 0; i < toSpawn; i++)
            {
                SpawnBackupUnit();
            }
        }

        if (_currentIncident.WantedLevel >= 3)
        {
            SpawnHelicopterIfNecessary();
        }
    }

    private void SpawnBackupUnit()
    {
        if (_currentIncident == null)
            return;

        int currentTotal = _currentIncident.Cops.Count + _spawnedCops.Count;
        if (currentTotal >= MaxSpawnedCops)
            return;

        Vector3 incidentPos = _currentIncident.Location;
        Vector3 offset = GetRandomOffsetOnCircle(BackupSpawnRadiusMin, BackupSpawnRadiusMax);
        Vector3 spawnPos = World.GetNextPositionOnStreet(incidentPos + offset);
        if (spawnPos == Vector3.Zero)
            spawnPos = incidentPos + offset;

        bool useSwat = _currentIncident.WantedLevel >= 4;
        bool isCity = IsCityArea(_currentIncident.Location);

        string modelName;
        if (useSwat)
        {
            modelName = _swatVehicleModels[_rng.Next(_swatVehicleModels.Length)];
        }
        else
        {
            string[] pool = isCity ? _cityPoliceVehicleModels : _countyPoliceVehicleModels;
            modelName = pool[_rng.Next(pool.Length)];
        }

        Model vehModel = new Model(modelName);
        if (!vehModel.IsInCdImage || !vehModel.IsValid)
            return;

        vehModel.Request(500);
        if (!vehModel.IsLoaded)
            return;

        Vehicle veh = World.CreateVehicle(vehModel, spawnPos);
        if (veh == null || !veh.Exists())
            return;

        _spawnedVehicles.Add(veh);

        string pedModelName = useSwat ? "s_m_y_swat_01" : "s_m_y_cop_01";
        Model copModel = new Model(pedModelName);
        copModel.Request(500);
        if (!copModel.IsInCdImage || !copModel.IsValid || !copModel.IsLoaded)
        {
            veh.MarkAsNoLongerNeeded();
            return;
        }

        Ped driver = veh.CreatePedOnSeat(VehicleSeat.Driver, copModel);
        if (driver == null || !driver.Exists())
        {
            veh.MarkAsNoLongerNeeded();
            return;
        }

        SetupCop(driver, useSwat);
        _spawnedCops.Add(driver);
        _currentIncident.Cops.Add(driver);

        if (useSwat)
        {
            VehicleSeat[] swatSeats =
            {
                VehicleSeat.ExtraSeat1,
                VehicleSeat.ExtraSeat2,
                VehicleSeat.Passenger,
                VehicleSeat.LeftRear,
                VehicleSeat.RightRear
            };

            foreach (VehicleSeat seat in swatSeats)
            {
                int total = _currentIncident.Cops.Count + _spawnedCops.Count;
                if (total >= MaxSpawnedCops)
                    break;

                if (!veh.IsSeatFree(seat))
                    continue;

                Ped swatPed = veh.CreatePedOnSeat(seat, copModel);
                if (swatPed != null && swatPed.Exists())
                {
                    SetupCop(swatPed, true);
                    _spawnedCops.Add(swatPed);
                    _currentIncident.Cops.Add(swatPed);

                    Ped targetForSwat = GetClosestSuspect(swatPed.Position);
                    if (targetForSwat != null)
                    {
                        TaskCopAttack(swatPed, targetForSwat);
                    }
                }
            }
        }

        Ped target = GetClosestSuspect(driver.Position);
        if (target != null)
        {
            TaskCopAttack(driver, target);
        }
    }

    private void SpawnHelicopterIfNecessary()
    {
        if (_currentIncident == null)
            return;

        int maxHelis = _currentIncident.WantedLevel >= 4 ? 2 : 1;

        int heliCount = 0;
        foreach (Vehicle v in _spawnedVehicles)
        {
            if (v != null && v.Exists() && v.Model.IsHelicopter)
                heliCount++;
        }

        if (heliCount >= maxHelis)
            return;

        Vector3 incidentPos = _currentIncident.Location;
        Vector3 horizOffset = GetRandomOffsetOnCircle(80f, 150f);
        Vector3 spawnPos = incidentPos + horizOffset + new Vector3(0f, 0f, 60f);

        Model heliModel = new Model(VehicleHash.Polmav);
        if (!heliModel.IsInCdImage || !heliModel.IsValid)
            return;

        heliModel.Request(1000);
        if (!heliModel.IsLoaded)
            return;

        Vehicle heli = World.CreateVehicle(heliModel, spawnPos);
        if (heli == null || !heli.Exists())
            return;

        _spawnedVehicles.Add(heli);

        Model pilotModel = new Model("s_m_y_cop_01");
        pilotModel.Request(1000);
        if (!pilotModel.IsInCdImage || !pilotModel.IsValid || !pilotModel.IsLoaded)
        {
            heli.MarkAsNoLongerNeeded();
            return;
        }

        Ped pilot = heli.CreatePedOnSeat(VehicleSeat.Driver, pilotModel);
        if (pilot == null || !pilot.Exists())
        {
            heli.MarkAsNoLongerNeeded();
            return;
        }

        SetupCop(pilot, false);

        _spawnedCops.Add(pilot);
        _currentIncident.Cops.Add(pilot);

        Ped target = GetClosestSuspect(heli.Position);
        if (target != null)
        {
            pilot.Task.VehicleChase(target);
        }
    }

    private void SetupCop(Ped cop, bool swat)
    {
        if (cop == null || !cop.Exists())
            return;

        cop.Armor = swat ? 200 : 100;
        cop.CanSwitchWeapons = true;

        WeaponHash weapon = swat ? WeaponHash.CarbineRifle : WeaponHash.Pistol;
        int ammo = swat ? 300 : 200;

        cop.Weapons.Give(weapon, ammo, true, true);
        cop.Weapons.Select(weapon);

        cop.Health = swat ? 250 : 200;
    }

    private Ped GetClosestSuspect(Vector3 fromPos)
    {
        if (_currentIncident == null || _currentIncident.Suspects.Count == 0)
            return null;

        Ped best = null;
        float bestDist = float.MaxValue;

        foreach (Ped s in _currentIncident.Suspects)
        {
            if (s == null || !s.Exists() || s.IsDead)
                continue;

            float d = fromPos.DistanceTo(s.Position);
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
            }
        }

        return best;
    }

    private void TaskCopAttack(Ped cop, Ped target)
    {
        if (cop == null || !cop.Exists() || cop.IsDead)
            return;

        if (target == null || !target.Exists() || target.IsDead)
            return;

        Function.Call(Hash.TASK_COMBAT_PED, cop.Handle, target.Handle, 0, 16);
    }

    private bool IsCop(Ped ped)
    {
        if (ped == null || !ped.Exists() || ped.IsDead || !ped.IsHuman)
            return false;

        PedHash model = (PedHash)ped.Model.Hash;

        return model == PedHash.Cop01SMY ||
        model == PedHash.Cop01SFY ||
        model == PedHash.Sheriff01SMY ||
        model == PedHash.Sheriff01SFY ||
        model == PedHash.Swat01SMY;
    }

    private bool IsPotentialSuspect(Ped ped, Ped player)
    {
        if (ped == null || !ped.Exists())
            return false;

        if (ped.IsDead || !ped.IsHuman)
            return false;

        if (ped == player)
            return false;

        if (IsCop(ped))
            return false;

        return true;
    }

    private bool IsCityArea(Vector3 pos)
    {
        Vector3 cityCenter = new Vector3(0f, -1000f, 0f);
        return pos.DistanceTo(cityCenter) < 3500f;
    }

    private Vector3 GetRandomOffsetOnCircle(float minRadius, float maxRadius)
    {
        double angle = _rng.NextDouble() * Math.PI * 2.0;
        double radius = minRadius + _rng.NextDouble() * (maxRadius - minRadius);

        float x = (float)(Math.Cos(angle) * radius);
        float y = (float)(Math.Sin(angle) * radius);

        return new Vector3(x, y, 0f);
    }

    private void CleanupIncident()
    {
        _currentIncident = null;

        foreach (Ped cop in _spawnedCops)
        {
            if (cop != null && cop.Exists())
                cop.MarkAsNoLongerNeeded();
        }

        foreach (Vehicle veh in _spawnedVehicles)
        {
            if (veh != null && veh.Exists())
                veh.MarkAsNoLongerNeeded();
        }

        _spawnedCops.Clear();
        _spawnedVehicles.Clear();
    }
}

internal class NpcIncident
{
    public Vector3 Location;
    public readonly List<Ped> Cops = new List<Ped>();
    public readonly List<Ped> Suspects = new List<Ped>();

    public int WantedLevel;
    public int LastSeenTime;
    public int LastCombatTime;
    public int CopsKilled;

    public NpcIncident(Vector3 location)
    {
        Location = location;
    }
}
