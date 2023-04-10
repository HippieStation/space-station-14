using System.Linq;
using Content.Shared.Radiation.Components;
using JetBrains.Annotations;
using Content.Server.Supermatter.Components;
using Robust.Shared.Containers;
using Content.Shared.Damage;
using Content.Shared.Tag;
using Content.Shared.Projectiles;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Explosion.Components;
using Robust.Shared.Physics.Events;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Player;
using Content.Shared.Atmos;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Physics;

namespace Content.Server.Supermatter.EntitySystems
{
    [UsedImplicitly]
    public sealed class SupermatterSystem : EntitySystem
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly DamageableSystem _damageable = default!;
        [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
        [Dependency] private readonly ChatSystem _chat = default!;
        [Dependency] private readonly TagSystem _tag = default!;
        [Dependency] private readonly SharedContainerSystem _container = default!;
        [Dependency] private readonly ExplosionSystem _explosion = default!;
        [Dependency] private readonly TransformSystem _xform = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;

        public enum DelamType : sbyte
        {
            Explosion = 0,
            Singulo = 1,
        }

        public enum SuperMatterSound : sbyte
        {
            Calm = 0,
            Aggressive = 1,
            Delam = 2
        }

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<SupermatterComponent, StartCollideEvent>(OnCollideEvent);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            foreach (var (supermatter, damageable, xplode,rads) in EntityManager.EntityQuery<SupermatterComponent, DamageableComponent, ExplosiveComponent, RadiationSourceComponent>())
            {
                HandleOutput(supermatter.Owner, frameTime, supermatter, rads);
                HandleDamage(supermatter.Owner, frameTime, supermatter, damageable, xplode);
            }
        }

        /// <summary>
        /// Handle outputting based off enery, damage, gas mix and radiation
        /// </summary>
        private void HandleOutput(EntityUid uid, float frameTime, SupermatterComponent? sMcomponent = null, RadiationSourceComponent? radcomponent = null)
        {
            if(!Resolve(uid, ref sMcomponent, ref radcomponent))
            {
                return;
            }

            sMcomponent.AtmosUpdateAccumulator += frameTime;
            if (!(sMcomponent.AtmosUpdateAccumulator > sMcomponent.AtmosUpdateTimer) ||
                _atmosphere.GetContainingMixture(uid, true, true) is not { } mixture)
                return;

            sMcomponent.AtmosUpdateAccumulator -= sMcomponent.AtmosUpdateTimer;

            //Absorbed gas from surrounding area
            var absorbedGas = mixture.Remove(sMcomponent.GasEfficiency * mixture.TotalMoles);
            var absorbedTotalMoles = absorbedGas.TotalMoles;

            if (!(absorbedTotalMoles > 0f))
                return;

            var gasStorage = sMcomponent.GasStorage;
            var gasEffect = sMcomponent.GasDataFields;

            //Lets get the proportions of the gasses in the mix for scaling stuff later
            //They range between 0 and 1
            gasStorage = gasStorage.ToDictionary(
                gas => gas.Key,
                gas => Math.Clamp(absorbedGas.GetMoles(gas.Key) / absorbedTotalMoles, 0, 1)
            );

            //No less then zero, and no greater then one, we use this to do explosions
            //and heat to power transfer
            var gasmixPowerRatio = gasStorage.Sum(gas => gasStorage[gas.Key] * gasEffect[gas.Key].PowerMixRatio);

            //Minimum value of -10, maximum value of 23. Effects plasma and o2 output
            //and the output heat
            var dynamicHeatModifier = gasStorage.Sum(gas => gasStorage[gas.Key] * gasEffect[gas.Key].HeatPenalty);

            //Minimum value of -10, maximum value of 23. Effects plasma and o2 output
            // and the output heat
            var powerTransmissionBonus = gasStorage.Sum(gas => gasStorage[gas.Key] * gasEffect[gas.Key].TransmitModifier);

            var h2OBonus = 1 - (gasStorage[Gas.WaterVapor] * 0.25f);

            gasmixPowerRatio = Math.Clamp(gasmixPowerRatio, 0, 1);
            dynamicHeatModifier = Math.Max(dynamicHeatModifier, 0.5f);
            powerTransmissionBonus *= h2OBonus;

            //Effects the damage heat does to the crystal
            sMcomponent.DynamicHeatResistance = 1f;

            //more moles of gases are harder to heat than fewer,
            //so let's scale heat damage around them
            sMcomponent.MoleHeatPenaltyThreshold = (float)(Math.Max(absorbedTotalMoles / sMcomponent.MoleHeatPenalty, 0.25));

            //Ramps up or down in increments of 0.02 up to the proportion of co2
            //Given infinite time, powerloss_dynamic_scaling = co2comp
            //Some value between 0 and 1
            if(absorbedTotalMoles > sMcomponent.PowerlossInhibitionMoleThreshold && gasStorage[Gas.CarbonDioxide] > sMcomponent.PowerlossInhibitionGasThreshold)
            {
                sMcomponent.PowerlossDynamicScaling = Math.Clamp(sMcomponent.PowerlossDynamicScaling + Math.Clamp(gasStorage[Gas.CarbonDioxide] - sMcomponent.PowerlossDynamicScaling, -0.02f, 0.02f), 0f, 1f);
            }
            else
            {
                sMcomponent.PowerlossDynamicScaling = Math.Clamp(sMcomponent.PowerlossDynamicScaling - 0.05f, 0f, 1f);
            }

            //Ranges from 0 to 1(1-(value between 0 and 1 * ranges from 1 to 1.5(mol / 500)))
            //We take the mol count, and scale it to be our inhibitor
            var powerlossInhibitor = Math.Clamp(1-(sMcomponent.PowerlossDynamicScaling * Math.Clamp(absorbedTotalMoles/sMcomponent.PowerlossInhibitionMoleBoostThreshold, 1f, 1.5f)), 0f, 1f);

            if (sMcomponent.MatterPower != 0) //We base our removed power off one 10th of the matter_power.
            {
                var removedMatter = Math.Max(sMcomponent.MatterPower / sMcomponent.MatterPowerConversion, 40);
                //Adds at least 40 power
                sMcomponent.Power = Math.Max(sMcomponent.Power + removedMatter, 0);
                //Removes at least 40 matter power
                sMcomponent.MatterPower = Math.Max(sMcomponent.MatterPower - removedMatter, 0);
            }

            //based on gas mix, makes the power more based on heat or less effected by heat
            var tempFactor = gasmixPowerRatio > 0.8 ? 50f : 30f;

            //if there is more pluox and n2 then anything else, we receive no power increase from heat
            sMcomponent.Power = Math.Max((absorbedGas.Temperature * tempFactor / Atmospherics.T0C) * gasmixPowerRatio + sMcomponent.Power, 0);

            //Rad Pulse Calculation
            var rads = sMcomponent.Power * Math.Max(0, (1f + (powerTransmissionBonus/10f)));
            radcomponent.Intensity=rads * 0.003f;

            //Power * 0.55 * a value between 1 and 0.8
            var energy = sMcomponent.Power * sMcomponent.ReactionPowerModefier;

            //Keep in mind we are only adding this temperature to (efficiency)% of the one tile the rock
            //is on. An increase of 4*C @ 25% efficiency here results in an increase of 1*C / (#tilesincore) overall.
            //Power * 0.55 * (some value between 1.5 and 23) / 5

            absorbedGas.Temperature += ((energy * dynamicHeatModifier) / sMcomponent.ThermalReleaseModifier);
            absorbedGas.Temperature = Math.Max(0, Math.Min(absorbedGas.Temperature, sMcomponent.HeatThreshold * dynamicHeatModifier));

            //Calculate how much gas to release
            //Varies based on power and gas content

            absorbedGas.AdjustMoles(Gas.Plasma,
                Math.Max((energy * dynamicHeatModifier) / sMcomponent.PlasmaReleaseModifier, 0f));

            absorbedGas.AdjustMoles(Gas.Oxygen,
                Math.Max(((energy + absorbedGas.Temperature * dynamicHeatModifier) - Atmospherics.T0C) / sMcomponent.OxygenReleaseModifier, 0f));

            _atmosphere.Merge(mixture,absorbedGas);

            sMcomponent.Mix = mixture;

            var powerReduction = (float)Math.Pow((sMcomponent.Power/500), 3);

            //After this point power is lowered
            //This wraps around to the begining of the function
            sMcomponent.Power = Math.Max(sMcomponent.Power - Math.Min(powerReduction * powerlossInhibitor, sMcomponent.Power * 0.83f * powerlossInhibitor) * 1, 0f);
        }

        /// <summary>
        /// Handles environmental damage and dispatching damage warning
        /// </summary>
        private void HandleDamage(EntityUid uid, float frameTime, SupermatterComponent? sMcomponent = null, DamageableComponent? damageable = null, ExplosiveComponent? xplode = null)
        {
            if (!Resolve(uid, ref sMcomponent, ref damageable, ref xplode))
            {
                return;
            }

            var xform = Transform(uid);
            var grid = xform.GridUid;
            var indices = _xform.GetGridOrMapTilePosition(uid, xform);

            sMcomponent.DamageUpdateAccumulator += frameTime;
            sMcomponent.YellAccumulator += frameTime;

            if (!(sMcomponent.DamageUpdateAccumulator > sMcomponent.DamageUpdateTimer))
                return;

            var damage = 0f;
            var damageArchived = damageable.TotalDamage.Float();

            //gets the integrity as a percentage
            var integrity = (100 - (100 * (damageable.TotalDamage.Float() / sMcomponent.ExplosionPoint)));

            if (damageArchived >= sMcomponent.ExplosionPoint)
            {
                Delamination(uid, frameTime, damageArchived, sMcomponent, xplode);
                return;
            }

            if (sMcomponent.YellAccumulator >= sMcomponent.YellTimer)
            {
                if (damageArchived >= sMcomponent.EmergencyPoint && damageArchived <= sMcomponent.ExplosionPoint)
                {
                    _chat.TrySendInGameICMessage(uid, Loc.GetString("supermatter-warning-message", ("integrity", integrity.ToString("0.00"))), InGameICChatType.Speak, hideChat: true);
                    sMcomponent.YellAccumulator = 0;
                }
                if (damageArchived >= sMcomponent.WarningPoint && damageArchived <= sMcomponent.EmergencyPoint)
                {
                    _chat.TrySendInGameICMessage(uid, Loc.GetString("supermatter-danger-message", ("integrity", integrity.ToString("0.00"))), InGameICChatType.Speak, hideChat: true);
                    sMcomponent.YellAccumulator = 0;
                }
            }

            //if in space
            if (!xform.GridUid.HasValue)
            {
                damage = Math.Max((sMcomponent.Power / 1000) * sMcomponent.DamageIncreaseMultiplier, 0.1f);
            }

            //if in an atmosphere
            if (sMcomponent.Mix is { } mixture)
            {
                var moles = mixture.TotalMoles;
                //((((some value between 0.5 and 1 * temp - ((273.15 + 40) * some values between 1 and 10)) * some number between 0.25 and knock your socks off / 150) * 0.25
                //Heat and mols account for each other, a lot of hot mols are more damaging then a few
                //Mols start to have a positive effect on damage after 350
                var moleClamp = Math.Clamp(moles / 200f, 0.5f, 1f);
                var heatDamage = (Atmospherics.T0C + sMcomponent.HeatPenaltyThreshold)*sMcomponent.DynamicHeatResistance;
                damage = Math.Max(damage + (Math.Max(moleClamp * mixture.Temperature - heatDamage, 0) * sMcomponent.MoleHeatPenaltyThreshold / 150) * sMcomponent.DamageIncreaseMultiplier, 0f);

                //Power only starts affecting damage when it is above 5000
                damage = Math.Max(damage + (Math.Max(sMcomponent.Power - sMcomponent.PowerPenaltyThreshold, 0f)/500f) * sMcomponent.DamageIncreaseMultiplier, 0f);

                //Molar count only starts affecting damage when it is above 1800
                damage = Math.Max(damage + (Math.Max(moles - sMcomponent.MolePenaltyThreshold, 0f)/80f) * sMcomponent.DamageIncreaseMultiplier, 0f);

                //There might be a way to integrate healing and hurting via heat
                //healing damage
                if (moles < sMcomponent.MolePenaltyThreshold)
                {
                    //Only has a net positive effect when the temp is below 313.15, heals up to 2 damage. Psycologists increase this temp min by up to 45
                    damage = Math.Max(damage + (Math.Min(mixture.Temperature - ((Atmospherics.T0C + sMcomponent.HeatPenaltyThreshold)) , 0f) / 150f ), 0f);
                }

                //if there are space tiles next to SM
                //TODO: change moles out for checking if adjacent tiles exist
                if (grid is not null)
                {
                    foreach (var ind in _atmosphere.GetAdjacentTileMixtures(grid.Value, indices))
                    {
                        if (ind.TotalMoles != 0)
                            continue;

                        var factor = integrity switch
                        {
                            (<= 25) => 0.002f,
                            (<= 55) and (> 25) => 0.005f,
                            (<= 75) and (> 55) => 0.0009f,
                            (<= 90) and (> 75) => 0.0005f,
                            _ => 0,
                        };

                        damage = Math.Clamp((sMcomponent.Power * factor) * sMcomponent.DamageIncreaseMultiplier, 0f, sMcomponent.MaxSpaceExposureDamage);
                    }
                }
            }
            //only take up to 1.8 damage per cycle with no lower limmit
            damage = Math.Min(damageArchived + (sMcomponent.DamageHardcap * sMcomponent.ExplosionPoint), damage);
            //damage to add to total
            var damageDelta = new DamageSpecifier(_prototypeManager.Index<DamageTypePrototype>("Blunt"), damage);
            _damageable.TryChangeDamage(uid, damageDelta, true);

            sMcomponent.DamageUpdateAccumulator -= sMcomponent.DamageUpdateTimer;

            if(sMcomponent.SmSound==SuperMatterSound.Delam)
                return;

            HandleSoundLoop(uid, sMcomponent, damageable);
        }

        /// <summary>
        /// Runs the logic and timers for Delamination
        /// </summary>
        private void Delamination(EntityUid uid, float frameTime, float damageArchived, SupermatterComponent? sMcomponent = null, ExplosiveComponent? xplode = null)
        {
            if (!Resolve(uid, ref sMcomponent, ref xplode))
            {
                return;
            }

            var xform = Transform(uid);

            var sounds = sMcomponent.DelamAlarm;
            var sound = _audio.GetSound(sounds);
            var param = sounds.Params.WithLoop(true).WithVolume(5f).WithMaxDistance(20f);

            //before we actually start counting down, check to see what delam type we're doing.
            if (!sMcomponent.FinalCountdown)
            {
                //if we're in atmos
                if (sMcomponent.Mix is { } mixture)
                {
                    var moles = mixture.TotalMoles;
                    //if the moles on the sm's tile are above MolePenaltyThreshold
                    if (moles >= sMcomponent.MolePenaltyThreshold)
                    {
                        sMcomponent.DelamType = DelamType.Singulo;
                        _chat.TrySendInGameICMessage(uid, Loc.GetString("supermatter-delamination-overmass"), InGameICChatType.Speak, hideChat: true);
                    }
                }
                else
                {
                    sMcomponent.DelamType = DelamType.Explosion;
                    _chat.TrySendInGameICMessage(uid, Loc.GetString("supermatter-delamination-default"), InGameICChatType.Speak, hideChat: true);
                }
            }

            sMcomponent.FinalCountdown = true;

            sMcomponent.DelamTimerAccumulator += frameTime;
            sMcomponent.SpeakAccumulator += frameTime;
            var roundSeconds = sMcomponent.DelamTimerTimer - (int)Math.Floor(sMcomponent.DelamTimerAccumulator);

            //we healed out of delam, return
            if (damageArchived < sMcomponent.ExplosionPoint)
            {
                sMcomponent.FinalCountdown = false;
                _chat.TrySendInGameICMessage(uid, Loc.GetString("supermatter-safe-allert"), InGameICChatType.Speak, hideChat: true);
                return;
            }
            //we're more than 5 seconds from delam, only yell every 5 seconds.
            else if (roundSeconds >= sMcomponent.YellDelam && sMcomponent.SpeakAccumulator >= sMcomponent.YellDelam)
            {
                sMcomponent.SpeakAccumulator -= sMcomponent.YellDelam;
                _chat.TrySendInGameICMessage(uid, Loc.GetString("supermatter-seconds-before-delam",("Seconds", roundSeconds)), InGameICChatType.Speak, hideChat: true);
            }
            //less than 5 seconds to delam, count every second.
            else if (roundSeconds <  sMcomponent.YellDelam && sMcomponent.SpeakAccumulator >= 1)
            {
                sMcomponent.SpeakAccumulator -= 1;
                _chat.TrySendInGameICMessage(uid, Loc.GetString("supermatter-seconds-before-delam",("Seconds", roundSeconds)), InGameICChatType.Speak, hideChat: true);
            }

            //play an alarm as long as you're delaming
            if (sMcomponent.FinalCountdown && sMcomponent.SmSound != SuperMatterSound.Delam)
            {
                sMcomponent.Stream?.Stop();
                sMcomponent.Stream = _audio.Play(sound, Filter.Pvs(uid, 5), uid, false, param);
                sMcomponent.SmSound = SuperMatterSound.Delam;
            }

            //TODO: make tesla(?) spawn at SupermatterComponent.PowerPenaltyThreshold and think up other delam types
            //times up, explode or make a singulo
            if (!(sMcomponent.DelamTimerAccumulator >= sMcomponent.DelamTimerTimer))
                return;

            if (sMcomponent.DelamType==DelamType.Singulo)
            {
                //spawn a singulo :)
                EntityManager.SpawnEntity("Singularity", xform.Coordinates);
                sMcomponent.Stream?.Stop();
            }
            else
            {
                //explosion!!!!!
                _explosion.TriggerExplosive(
                    uid,
                    explosive: xplode,
                    totalIntensity: sMcomponent.TotalIntensity,
                    radius: sMcomponent.Radius,
                    user: uid
                );
                sMcomponent.Stream?.Stop();
            }

            sMcomponent.FinalCountdown = false;
        }

        /// <summary>
        /// Determines if an entity can be dusted
        /// </summary>
        private bool CannotDestroy(EntityUid uid)
        {
            var @static = false;

            var tag = _tag.HasTag(uid, "SMImmune");

            if(EntityManager.TryGetComponent<PhysicsComponent>(uid, out var physicsComp))
            {
                @static = (physicsComp.BodyType == BodyType.Static);
            }

            return tag || @static;
        }

        private void OnCollideEvent(EntityUid uid, SupermatterComponent supermatter, ref StartCollideEvent args)
        {
            var target = args.OtherFixture.Body.Owner;

            if (!supermatter.Whitelist.IsValid(target) || CannotDestroy(target) || _container.IsEntityInContainer(uid))
                return;

            if (EntityManager.TryGetComponent<SupermatterFoodComponent>(target, out var supermatterFood))
                supermatter.Power += supermatterFood.Energy;
            else if (EntityManager.TryGetComponent<ProjectileComponent>(target, out var projectile))
                supermatter.Power += (float) projectile.Damage.Total;
            else
                supermatter.Power++;

            supermatter.MatterPower += EntityManager.HasComponent<MobStateComponent>(target) ? 200 : 0;
            if (!EntityManager.HasComponent<ProjectileComponent>(target))
            {
                _audio.Play(supermatter.DustSound, Filter.Pvs(uid), uid, false);
                EntityManager.SpawnEntity("Ash", Transform(target).Coordinates);
            }

            EntityManager.QueueDeleteEntity(target);
        }

        private void HandleSoundLoop(EntityUid uid, SupermatterComponent sMcomponent,DamageableComponent damageable)
        {
            var isAggressive = damageable.TotalDamage.Float() >= 50;
            var smSound = isAggressive ? SuperMatterSound.Aggressive : SuperMatterSound.Calm;

            if (sMcomponent.SmSound == smSound)
                return;

            sMcomponent.Stream?.Stop();
            var sounds = isAggressive ? sMcomponent.DelamSound : sMcomponent.CalmSound;
            var sound = _audio.GetSound(sounds);
            var param = sounds.Params.WithLoop(true).WithVolume(5f).WithMaxDistance(20f);
            sMcomponent.Stream = _audio.Play(sound, Filter.Pvs(uid), uid, false, param);
            sMcomponent.SmSound = smSound;
        }
    }
}
