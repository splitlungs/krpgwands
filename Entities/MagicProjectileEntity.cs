using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace krpgwands
{
    /// <summary>
    /// Cloned from EntityProjectile.cs
    /// </summary>
    public class MagicProjectileEntity : Entity
    {
        public static SimpleParticleProperties particles = new SimpleParticleProperties(
            1, 1,
            ColorUtil.ColorFromRgba(220, 220, 220, 50),
            new Vec3d(),
            new Vec3d(),
            new Vec3f(-0.25f, 0.1f, -0.25f),
            new Vec3f(0.25f, 0.1f, 0.25f),
            1.5f,
            -0.075f,
            0.25f,
            0.25f,
            EnumParticleModel.Quad
        );
        
        public IServerNetworkChannel serverChannel;

        bool beforeCollided;
        bool stuck;

        long msLaunch;
        long msCollide;

        Vec3d motionBeforeCollide = new Vec3d();

        CollisionTester collTester = new CollisionTester();

        public Entity FiredBy;
        public float Weight = 0.1f;
        public float Damage;
        // public ItemStack ProjectileStack;
        // public float DropOnImpactChance = 0f;
        // public bool DamageStackOnImpact = false;
        public int ignite;
        public int chilling;
        public int lightning;
        public int pit;

        Cuboidf collisionTestBox;

        EntityPartitioning ep;


        #region Default Class
        public override bool ApplyGravity
        {
            get { return !stuck; }
        }

        public override bool IsInteractable
        {
            get { return false; }
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            // wandSystem = api.ModLoader.GetModSystem("krpgwands");

            msLaunch = World.ElapsedMilliseconds;

            collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);

            //if (api.Side == EnumAppSide.Server) - why only server side? This makes arrows fly through entities on the client
            {
                GetBehavior<EntityBehaviorPassivePhysics>().OnPhysicsTickCallback = onPhysicsTickCallback;
                ep = api.ModLoader.GetModSystem<EntityPartitioning>();
            }

            GetBehavior<EntityBehaviorPassivePhysics>().collisionYExtra = 0f; // Slightly cheap hax so that stones/arrows don't collid with fences
        }

        private void onPhysicsTickCallback(float dtFac)
        {
            if (ShouldDespawn || !Alive) return;
            if (World.ElapsedMilliseconds <= msCollide + 500) return;

            var pos = SidedPos;

            if (pos.Motion.X == 0 && pos.Motion.Y == 0 && pos.Motion.Z == 0) return;  // don't do damage if stuck in ground


            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(pos.X, pos.Y, pos.Z);

            if (pos.Motion.X < 0) projectileBox.X1 += pos.Motion.X * dtFac;
            else projectileBox.X2 += pos.Motion.X * dtFac;
            if (pos.Motion.Y < 0) projectileBox.Y1 += pos.Motion.Y * dtFac;
            else projectileBox.Y2 += pos.Motion.Y * dtFac;
            if (pos.Motion.Z < 0) projectileBox.Z1 += pos.Motion.Z * dtFac;
            else projectileBox.Z2 += pos.Motion.Z * dtFac;

            ep.WalkEntityPartitions(pos.XYZ, 5f, (e) =>
            {
                if (e.EntityId == this.EntityId || !e.IsInteractable || (FiredBy != null && e.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500)) return true;

                Cuboidd eBox = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z);

                if (eBox.IntersectsOrTouches(projectileBox))
                {
                    impactOnEntity(e);
                    return false;
                }

                return true;
            });
        }


        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (ShouldDespawn) return;

            EntityPos pos = SidedPos;

            stuck = Collided || collTester.IsColliding(World.BlockAccessor, collisionTestBox, pos.XYZ) || WatchedAttributes.GetBool("stuck");
            if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", stuck);

            double impactSpeed = Math.Max(motionBeforeCollide.Length(), pos.Motion.Length());

            if (stuck)
            {
                if (Api.Side == EnumAppSide.Client) ServerPos.SetFrom(Pos);
                IsColliding(pos, impactSpeed);
                return;
            }
            else
            {
                SetRotation();
            }

            if (TryAttackEntity(impactSpeed))
            {
                return;
            }

            Random rand = new Random();
            particles.Color = ColorUtil.ColorFromRgba(rand.Next(0, 255), rand.Next(0, 255), rand.Next(0, 255), 255);
            // particles.MinPos = pos.AheadCopy(0.8, byEntity.Pos.Pitch, byEntity.Pos.Yaw);
            particles.AddPos.Set(0.1, 0.1, 0.1);
            particles.MinSize = 0.1F;
            particles.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.SINUS, 5);
            this.Api.World.SpawnParticles(particles);

            beforeCollided = false;
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }


        public override void OnCollided()
        {
            EntityPos pos = SidedPos;

            IsColliding(SidedPos, Math.Max(motionBeforeCollide.Length(), pos.Motion.Length()));
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }


        private void IsColliding(EntityPos pos, double impactSpeed)
        {
            pos.Motion.Set(0, 0, 0);

            if (!beforeCollided && World is IServerWorldAccessor && World.ElapsedMilliseconds > msCollide + 500)
            {
                /*
                if (impactSpeed >= 0.07)
                {
                    World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, false, 32);
                    
                    // Resend position to client
                    WatchedAttributes.MarkAllDirty();
                    
                    Die();
                
                    if (DamageStackOnImpact)
                    {
                        ProjectileStack.Collectible.DamageItem(World, this, new DummySlot(ProjectileStack));
                        int leftDurability = ProjectileStack == null ? 1 : ProjectileStack.Collectible.GetRemainingDurability(ProjectileStack);
                        if (leftDurability <= 0)
                        {
                            Die();
                        }
                    }
                }
                */

                TryAttackEntity(impactSpeed);

                // TryActivateEffect();
                // The shamman calls the lightning, set by the caster
                if (lightning > 0)
                    CallLightning(pos);

                if (pit > 0)
                    CreatePit(pos, FiredBy, pit, pit);

                msCollide = World.ElapsedMilliseconds;

                beforeCollided = true;
            }
        }

        bool TryAttackEntity(double impactSpeed)
        {
            if (World is IClientWorldAccessor || World.ElapsedMilliseconds <= msCollide + 250) return false;
            if (impactSpeed <= 0.01) return false;

            EntityPos pos = SidedPos;

            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(ServerPos.X, ServerPos.Y, ServerPos.Z);

            // We give it a bit of extra leeway of 50% because physics ticks can run twice or 3 times in one game tick 
            if (ServerPos.Motion.X < 0) projectileBox.X1 += 1.5 * ServerPos.Motion.X;
            else projectileBox.X2 += 1.5 * ServerPos.Motion.X;
            if (ServerPos.Motion.Y < 0) projectileBox.Y1 += 1.5 * ServerPos.Motion.Y;
            else projectileBox.Y2 += 1.5 * ServerPos.Motion.Y;
            if (ServerPos.Motion.Z < 0) projectileBox.Z1 += 1.5 * ServerPos.Motion.Z;
            else projectileBox.Z2 += 1.5 * ServerPos.Motion.Z;

            Entity entity = World.GetNearestEntity(ServerPos.XYZ, 5f, 5f, (e) => {
                if (e.EntityId == this.EntityId || !e.IsInteractable) return false;

                if (FiredBy != null && e.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500)
                {
                    return false;
                }

                Cuboidd eBox = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z);

                return eBox.IntersectsOrTouches(projectileBox);
            });

            if (entity != null)
            {
                impactOnEntity(entity);
                return true;
            }


            return false;
        }


        private void impactOnEntity(Entity entity)
        {
            if (!Alive) return;

            EntityPos pos = SidedPos;

            IServerPlayer fromPlayer = null;
            if (FiredBy is EntityPlayer)
            {
                fromPlayer = (FiredBy as EntityPlayer).Player as IServerPlayer;
            }

            bool targetIsPlayer = entity is EntityPlayer;
            bool targetIsCreature = entity is EntityAgent;
            bool canDamage = true;

            ICoreServerAPI sapi = World.Api as ICoreServerAPI;
            if (fromPlayer != null)
            {
                if (targetIsPlayer && (!sapi.Server.Config.AllowPvP || !fromPlayer.HasPrivilege("attackplayers"))) canDamage = false;
                if (targetIsCreature && !fromPlayer.HasPrivilege("attackcreatures")) canDamage = false;
            }

            msCollide = World.ElapsedMilliseconds;

            pos.Motion.Set(0, 0, 0);

            if (canDamage && World.Side == EnumAppSide.Server)
            {
                World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, false, 24);

                float dmg = Damage;
                if (FiredBy != null) dmg *= FiredBy.Stats.GetBlended("rangedWeaponsDamage");

                bool didDamage = entity.ReceiveDamage(new DamageSource()
                {
                    Source = EnumDamageSource.Entity,
                    SourceEntity = FiredBy == null ? this : FiredBy,
                    Type = EnumDamageType.PiercingAttack
                }, dmg);

                float kbresist = entity.Properties.KnockbackResistance;
                entity.SidedPos.Motion.Add(kbresist * pos.Motion.X * Weight, kbresist * pos.Motion.Y * Weight, kbresist * pos.Motion.Z * Weight);


                // Gather all of the KRPG Stats for processing. This should probably be elsehwere
                // KRPGStatsBehavior krpgStats = entity.GetBehavior("KRPGStats") as KRPGStatsBehavior;
                float fireResist = entity.Attributes.GetFloat("fireResist", 0);
                float lightningResist = entity.Attributes.GetFloat("lightningResist", 0);

                // It should default to 0f. Ignition should stop at 0.5. Absorbtion should start at 1f.
                // if (fireResist <= 0.5f)
                if (ignite > 0)
                    IgniteEntity(entity);

                if (chilling > 0)
                    ChillEntity(entity, chilling);

                // It should default to 0f. Stun should stop at 0.5. Absorbtion should start at 1f.
                // if (lightningResist <= 0.5f)
                // The shamman calls the lightning, set by the caster
                if (lightning > 0)
                    CallLightning(pos);

                if (pit > 0)
                    CreatePit(pos, entity, pit, pit);


                /*
                int leftDurability = 1;
                if (DamageStackOnImpact)
                {
                    ProjectileStack.Collectible.DamageItem(entity.World, entity, new DummySlot(ProjectileStack));
                    leftDurability = ProjectileStack == null ? 1 : ProjectileStack.Collectible.GetRemainingDurability(ProjectileStack);
                }

                if (World.Rand.NextDouble() < DropOnImpactChance && leftDurability > 0)
                {

                }
                else
                {
                    Die();
                }
                */

                if (FiredBy is EntityPlayer && didDamage)
                {
                    World.PlaySoundFor(new AssetLocation("sounds/player/projectilehit"), (FiredBy as EntityPlayer).Player, false, 24);
                }
            }
        }

        public virtual void SetRotation()
        {
            EntityPos pos = (World is IServerWorldAccessor) ? ServerPos : Pos;

            double speed = pos.Motion.Length();

            if (speed > 0.01)
            {
                pos.Pitch = 0;
                pos.Yaw =
                    GameMath.PI + (float)Math.Atan2(pos.Motion.X / speed, pos.Motion.Z / speed)
                    + GameMath.Cos((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
                pos.Roll =
                    -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1))
                    + GameMath.Sin((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
            }
        }


        /*
        public override bool CanCollect(Entity byEntity)
        {
            return Alive && World.ElapsedMilliseconds - msLaunch > 1000 && ServerPos.Motion.Length() < 0.01;
        }

        public override ItemStack OnCollected(Entity byEntity)
        {
            ProjectileStack.ResolveBlockOrItem(World);
            return ProjectileStack;
        }
        */


        public override void OnCollideWithLiquid()
        {
            base.OnCollideWithLiquid();
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            //ProjectileStack.ToBytes(writer);
        }

        public override void FromBytes(BinaryReader reader, bool fromServer)
        {
            base.FromBytes(reader, fromServer);
            beforeCollided = reader.ReadBoolean();
            //ProjectileStack = new ItemStack(reader);
        }
        #endregion

        #region Effects
        // Why are these hardcoded?
        // That's a good question. I have no excuses.
        // We'll get there. JSON config for effects soon™

        // Not in use yet, but probably should be to crustomize collision processing
        public bool TryEffects(Entity byEntity, float impactSpeed)
        {
            if (World is IClientWorldAccessor || World.ElapsedMilliseconds <= msCollide + 250) return false;
            if (impactSpeed <= 0.01) return false;

            EntityPos pos = SidedPos;

            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(ServerPos.X, ServerPos.Y, ServerPos.Z);

            // We give it a bit of extra leeway of 50% because physics ticks can run twice or 3 times in one game tick 
            if (ServerPos.Motion.X < 0) projectileBox.X1 += 1.5 * ServerPos.Motion.X;
            else projectileBox.X2 += 1.5 * ServerPos.Motion.X;
            if (ServerPos.Motion.Y < 0) projectileBox.Y1 += 1.5 * ServerPos.Motion.Y;
            else projectileBox.Y2 += 1.5 * ServerPos.Motion.Y;
            if (ServerPos.Motion.Z < 0) projectileBox.Z1 += 1.5 * ServerPos.Motion.Z;
            else projectileBox.Z2 += 1.5 * ServerPos.Motion.Z;

            Entity entity = World.GetNearestEntity(ServerPos.XYZ, 5f, 5f, (e) => {
                if (e.EntityId == this.EntityId || !e.IsInteractable) return false;

                if (FiredBy != null && e.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500)
                {
                    return false;
                }

                Cuboidd eBox = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z);

                return eBox.IntersectsOrTouches(projectileBox);
            });

            if (entity != null)
            {
                impactOnEntity(entity);
                return true;
            }


            return false;
        }
        public void ChillEntity(Entity entity, int power)
        {
            EntityBehaviorBodyTemperature ebbt = entity.GetBehavior<EntityBehaviorBodyTemperature>();

            // If we encounter something without one, bail
            if (ebbt == null)
                return;

            ebbt.CurBodyTemperature = power * -10f;

        }
        public void CreatePit(EntityPos pos, Entity byEntity, int depth, int width)
        {
            BlockPos bpos = pos.AsBlockPos;
            List<Vec3d> pitArea = new List<Vec3d>();

            for (int x = 0; x <= width; x++)
            {
                for (int y = 0; y <= depth; y++)
                {
                    for (int z = 0; z <= width; z++)
                    {
                        pitArea.Add(new Vec3d(bpos.X + x, bpos.Y - y, bpos.Z + z));
                        pitArea.Add(new Vec3d(bpos.X - x, bpos.Y - y, bpos.Z - z));
                        pitArea.Add(new Vec3d(bpos.X + x, bpos.Y - y, bpos.Z - z));
                        pitArea.Add(new Vec3d(bpos.X - x, bpos.Y - y, bpos.Z + z));
                    }
                }
            }

            for (int i = 0; i < pitArea.Count; i++)
            {
                BlockPos ipos = bpos;
                ipos.Set(pitArea[i]);
                Block block = World.BlockAccessor.GetBlock(ipos);

                if (block != null)
                {
                    string blockCode = block.Code.ToString();
                    if (blockCode.Contains("soil") || blockCode.Contains("sand") || blockCode.Contains("gravel"))
                        World.BlockAccessor.BreakBlock(ipos, byEntity as IPlayer);
                }
            }
        }
        public void IgniteEntity(Entity entity)
        {
            entity.IsOnFire = true;
        }
        public void CallLightning(EntityPos pos)
        {
            WeatherSystemServer weatherSystem = World.Api.ModLoader.GetModSystem<WeatherSystemServer>();
            // It should default to 0f. Stun should stop at 0.5. Absorbtion should start at 1f.
            weatherSystem.SpawnLightningFlash(pos.XYZ);
        }
        #endregion
    }
}