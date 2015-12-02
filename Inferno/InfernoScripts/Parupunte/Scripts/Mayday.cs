﻿using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;

namespace Inferno.InfernoScripts.Parupunte.Scripts
{
    /// <summary>
    /// プレイヤの近くに飛行機を墜落させる
    /// </summary>
    class Mayday : ParupunteScript
    {
        public Mayday(ParupunteCore core) : base(core)
        {
        }

        public override string Name => "メーデー！メーデー！";

        public override void OnStart()
        {
            StartCoroutine(AirPlaneCoroutine());
        }

        IEnumerable<object> AirPlaneCoroutine()
        {
            //飛行機生成
            var model = new Model(VehicleHash.Jet);
            var plane = GTA.World.CreateVehicle(model, core.PlayerPed.Position + new Vector3(0, -1000, 200));
            if (!plane.IsSafeExist()) yield break;
            plane.Speed = 300;
            plane.MarkAsNoLongerNeeded();

            //ラマー生成
            var ped = plane.CreatePedOnSeat(VehicleSeat.Driver, new Model(PedHash.LamarDavis));
            ped.MarkAsNoLongerNeeded();
            ped.Task.ClearAll();

            yield return WaitForSeconds(4);
            if (!plane.IsSafeExist() || !ped.IsSafeExist()) yield break;
            plane.EngineHealth = 0;
            plane.EngineRunning = false;

            //飛行機が壊れたら大爆発させる
            foreach (var s in WaitForSeconds(10))
            {
                if(!plane.IsSafeExist()) break;
                if (!plane.IsAlive)
                {
                    foreach (var i in Enumerable.Range(0,10))
                    {
                        if(!plane.IsSafeExist()) break;
                        var point = plane.Position.Around(10.0f);
                        GTA.World.AddExplosion(point, GTA.ExplosionType.Rocket, 20.0f, 1.5f);
                        yield return WaitForSeconds(0.2f);
                    }
                    break;
                }
                yield return null;
            }
            ParupunteEnd();
        } 
    }
}