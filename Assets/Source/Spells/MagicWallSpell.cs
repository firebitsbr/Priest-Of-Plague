﻿using JetBrains.Annotations;
using PriestOfPlague.Source.Hubs;
using PriestOfPlague.Source.Items;
using UnityEngine;
using UnityEngine.Assertions.Must;

namespace PriestOfPlague.Source.Spells
{
    public class MagicDamageWallSpell : AttackOrSupportSpellBase
    {
        public delegate void PerUnitCallbackType (Unit.Unit unit, SpellCastParameter parameter);

        public MagicDamageWallSpell (int id, Sprite icon, string info, bool movementRequired, bool affectSelf,
            ItemSuperType requiredItemSupertype, float requiredBaseCharge, float requiredChargePerLevel,
            float requiredBaseMovementPoints, float requiredMovementPointsPerLevel, float basicCastTime,
            float castTimeAdditionPerLevel, float baseAngle, float anglePerLevel, float baseDistance,
            float distancePerLevel, UnitCallbackType unitCallback) : 
            
            base (id, icon, info, movementRequired, affectSelf,
            requiredItemSupertype, requiredBaseCharge, requiredChargePerLevel, requiredBaseMovementPoints,
            requiredMovementPointsPerLevel, basicCastTime, castTimeAdditionPerLevel, baseAngle, anglePerLevel,
            baseDistance, distancePerLevel, unitCallback)
        {
        }

        public override void Cast (Unit.Unit caster, UnitsHub unitsHub, SpellCastParameter parameter)
        {
            parameter.UsedItem.Charge -= (RequiredBaseCharge + RequiredChargePerLevel * parameter.Level);
            caster.UseMovementPoints (RequiredBaseMovementPoints + RequiredMovementPointsPerLevel * parameter.Level);

            foreach (var unit in unitsHub.GetUnitsByCriteria (unitToCheck =>
                UnitsHubCriterias.MaxDistanceAndMaxAngle (caster, unitToCheck,
                    BaseDistance + DistancePerLevel * parameter.Level,
                    BaseAngle + AnglePerLevel * parameter.Level)))
            {
                if (AffectSelf || unit != caster)
                {
                    UnitCallback (unit, parameter);
                }
            }
        }
    }
}