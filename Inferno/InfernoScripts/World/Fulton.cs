﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;


namespace Inferno
{
    internal class Fulton : InfernoScript
    {
        protected override int TickInterval { get; } = 100;

        /// <summary>
        /// フルトン回収のコルーチン対象になっているEntity
        /// </summary>
        private HashSet<int> fulutonedEntityList = new HashSet<int>();
        private Queue<PedHash> motherBasePeds = new Queue<PedHash>(30);
        private Queue<GTA.Native.VehicleHash> motherbaseVeh = new Queue<GTA.Native.VehicleHash>(30);
        private Random random = new Random();

        /// <summary>
        /// フルトン回収で車を吊り下げた時の音
        /// </summary>
        private SoundPlayer soundPlayerVehicleSetup;

        /// <summary>
        /// フルトン回収で人を吊り下げた時の音
        /// </summary>
        private SoundPlayer soundPlayerPedSetup;

        /// <summary>
        /// 空に飛んで行く音
        /// </summary>
        private SoundPlayer soundPlayerMove;

        protected override void Setup()
        {
            CreateInputKeywordAsObservable("fulton")
                .Subscribe(_ =>
                {
                    IsActive = !IsActive;
                    DrawText("Fulton:" + IsActive, 3.0f);
                });

            OnAllOnCommandObservable.Subscribe(_ => IsActive = true);


            OnKeyDownAsObservable
                .Where(x => IsActive && x.KeyCode == Keys.F9 && motherbaseVeh.Count > 0)
                .Subscribe(_ => SpawnVehicle());

            OnKeyDownAsObservable
                .Where(x => IsActive && x.KeyCode == Keys.F10 && motherBasePeds.Count > 0)
                .Subscribe(_ => SpawnCitizen());

            OnTickAsObservable
                .Where(_ => IsActive)
                .Subscribe(_ => FulutonUpdate());

            //プレイヤが死んだらリストクリア
            OnTickAsObservable
                .Select(_ => PlayerPed.IsDead)
                .DistinctUntilChanged()
                .Where(x => x)
                .Subscribe(_ => fulutonedEntityList.Clear());
            SetUpSound();
        }

        /// <summary>
        /// 効果音のロード
        /// </summary>
        private void SetUpSound()
        {
            var filePaths = LoadWavFiles(@"scripts/fulton");
            var setupWav = filePaths.FirstOrDefault(x => x.Contains("vehicle.wav"));
            if (setupWav != null)
            {
                soundPlayerVehicleSetup = new SoundPlayer(setupWav);
            }

            setupWav = filePaths.FirstOrDefault(x => x.Contains("ped.wav"));
            if (setupWav != null)
            {
                soundPlayerPedSetup = new SoundPlayer(setupWav);
            }


            var moveWav = filePaths.FirstOrDefault(x => x.Contains("move.wav"));
            if (moveWav != null)
            {
                soundPlayerMove = new SoundPlayer(moveWav);
            }
        }

        private string[] LoadWavFiles(string targetPath)
        {
            if (!Directory.Exists(targetPath))
            {
                return new string[0];
            }

            return Directory.GetFiles(targetPath).Where(x => Path.GetExtension(x) == ".wav").ToArray();
        }

        #region 回収

        private void FulutonUpdate()
        {
            foreach (var entity in CachedPeds.Concat(CachedVehicles.Cast<Entity>()).Where(
                x => x.IsSafeExist()
                     && x.IsInRangeOf(PlayerPed.Position, 15.0f)
                     && !fulutonedEntityList.Contains(x.Handle)
                     && x.IsAlive
                ))
            {
                if (entity.HasBeenDamagedByPed(PlayerPed) &&(
                   entity.HasBeenDamagedBy(Weapon.UNARMED)
                    ))
                {
                    fulutonedEntityList.Add(entity.Handle);
                    StartCoroutine(FulutonCoroutine(entity));
                    if (entity is Vehicle)
                    {
                        soundPlayerVehicleSetup?.Play();
                    }
                    else
                    {
                        //pedの時は遅延させてならす
                        Observable.Timer(TimeSpan.FromSeconds(0.3f))
                            .Subscribe(_ => soundPlayerPedSetup?.Play());
                    }
                }
            }
        }

        private void LeaveAllPedsFromVehicle(Vehicle vec)
        {
            if (!vec.IsSafeExist()) return;

            foreach (
                var seat in
                    new[] {VehicleSeat.Driver, VehicleSeat.Passenger, VehicleSeat.LeftRear, VehicleSeat.RightRear})
            {
                var ped = vec.GetPedOnSeat(seat);
                if (ped.IsSafeExist())
                {
                    ped.Task.ClearAll();
                    ped.Task.ClearSecondary();
                    ped.Task.LeaveVehicle();
                }
            }
        }

        private IEnumerable<object> FulutonCoroutine(Entity entity)
        {
            //Entityが消え去った後に処理したいので先に情報を保存しておく
            int hash = -1;
            bool isPed = false;

            var upForce = new Vector3(0, 0, 1);
            if (entity is Ped)
            {
                var p = entity as Ped;
                p.CanRagdoll = true;
                p.SetToRagdoll(10*1000);

                isPed = true;
            }
            else
            {
                var v = entity as Vehicle;
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, v, 1);
                LeaveAllPedsFromVehicle(v);
            }
            hash = entity.Model.Hash;

            entity.ApplyForce(upForce * 2.0f);

            foreach (var s in WaitForSeconds(3))
            {
                if (!entity.IsSafeExist() || entity.IsDead) yield break;

                entity.ApplyForce(upForce*1.07f);

                yield return s;
            }

            if (!entity.IsSafeExist() || entity.IsRequiredForMission())
            {
                yield break;
            }

            if (PlayerPed.CurrentVehicle.IsSafeExist() && PlayerPed.CurrentVehicle.Handle == entity.Handle)
            {
                yield break;
            }

            //弾みをつける
            yield return WaitForSeconds(0.25f);
            soundPlayerMove?.Play();

 

            foreach (var s in WaitForSeconds(7))
            {
                if (!entity.IsSafeExist() || entity.Position.DistanceTo(PlayerPed.Position)>200)
                {
                    if (PlayerPed.CurrentVehicle.IsSafeExist() && PlayerPed.CurrentVehicle.Handle == entity.Handle)
                    {
                        yield break;
                    }

                    if (isPed)
                    {
                        motherBasePeds.Enqueue((PedHash) hash);
                        Game.Player.Money -= 100;
                    }
                    else
                    {
                        motherbaseVeh.Enqueue((GTA.Native.VehicleHash) hash);
                        Game.Player.Money -= 1000;
                    }
                    DrawText("回収完了", 3.0f);
                    yield break;
                }

                if (entity.IsDead) yield break;

                entity.ApplyForce(upForce*1.0f/Game.FPS*500.0f);

                yield return s;
            }
        }

        #endregion

        #region 生成

        private void SpawnCitizen()
        {
            var hash = motherBasePeds.Dequeue();

            var p = World.CreatePed(new Model(hash), PlayerPed.Position.AroundRandom2D(3.0f) + new Vector3(0, 0, 0.5f));
            if (!p.IsSafeExist()) return;
            var weapon = Enum.GetValues(typeof (WeaponHash))
                .Cast<WeaponHash>()
                .OrderBy(c => random.Next())
                .FirstOrDefault();

            var weaponhash = (int) weapon;
            p.MarkAsNoLongerNeeded();

            Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, p, Game.Player.GetPlayerGroup());
            p.SetDropWeaponWhenDead(false); //武器を落とさない
            p.GiveWeapon(weaponhash, 1000); //指定武器所持
            p.EquipWeapon(weaponhash); //武器装備
            p.Health = 1;
            p.Task.FightAgainstHatedTargets(50, 0);
            var blip = p.AddBlip();
            blip.Color = BlipColor.White;
            
        }

        private void SpawnVehicle()
        {
            var hash = motherbaseVeh.Dequeue();
            var vehicleGxtEntry = Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, (int)hash);
            DrawText(Game.GetGXTEntry(vehicleGxtEntry), 3.0f);
            StartCoroutine(SpawnVehicleCoroutine(new Model(hash), PlayerPed.Position.AroundRandom2D(20)));
        }

        private IEnumerable<object> SpawnVehicleCoroutine(Model model, Vector3 targetPosition)
        {
            var car = World.CreateVehicle(model, targetPosition + new Vector3(0, 0, 20));
            if (!car.IsSafeExist()) yield break;
            var upVector = new Vector3(0, 0, 1.0f);
            car.FreezePosition = false;
            car.Velocity = new Vector3();
            World.AddExplosion(targetPosition, GTA.ExplosionType.Flare, 1.0f, 0.0f);

            foreach (var s in WaitForSeconds(10))
            {
                if (!car.IsSafeExist()) yield break;
                car.ApplyForce(upVector);
                if (!car.IsInAir) break;
                yield return null;
            }

            if (!car.IsSafeExist()) yield break;
            car.MarkAsNoLongerNeeded();

        }

        #endregion
    }
}
