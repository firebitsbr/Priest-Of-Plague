﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using PriestOfPlague.Source.Core;
using PriestOfPlague.Source.Hubs;
using PriestOfPlague.Source.Items;
using PriestOfPlague.Source.Spells;
using PriestOfPlague.Source.Unit.Ai;
using UnityEngine;
using Random = System.Random;

namespace PriestOfPlague.Source.Unit
{
    public enum CharacteristicsEnum
    {
        Vitality = 0,
        Luck,
        Agility,
        Strength,
        Intelligence,
        Count
    }

    public class Unit : CreationInformer
    {
        public const string EventSpellLearned = "SpellLearned";
        public const string EventSpellForgotten = "SpellForgotten";

        public class SpellLearnedOrForgottenEventData
        {
            public SpellLearnedOrForgottenEventData (Unit eventUnit, int spellId)
            {
                EventUnit = eventUnit;
                SpellId = spellId;
            }

            public Unit EventUnit;
            public int SpellId;
        }

        public class AppliedModifier
        {
            public AppliedModifier (int id, float time, int level)
            {
                Id = id;
                Time = time;
                Level = level;
            }

            public int Id { get; set; }
            public float Time { get; set; }
            public int Level { get; set; }
        }

        public CharacterModifiersContainer CharacterModifiersContainerRef;
        public LineagesContainer LineagesContainerRef;
        public ItemTypesContainer ItemTypesContainerRef;
        public ItemsRegistrator ItemsRegistratorRef;
        public SpellsContainer SpellsContainerRef;
        public UnitsHub UnitsHubRef;

        public int Id { get; private set; }
        public int Alignment { get; private set; }
        public bool Alive { get; private set; }

        public string Name { get; private set; }
        public bool IsMan { get; private set; }
        public Storage MyStorage { get; private set; }
        public Equipment MyEquipment { get; private set; }

        public float NearDamageBust { get; private set; }
        public float OnDistanceDamageBust { get; private set; }
        public float MagicDamageBust { get; private set; }
        public float CriticalDamageChance { get; private set; }
        public float CriticalResistChance { get; private set; }

        public float CurrentHp { get; private set; }
        public float MaxHp { get; private set; }
        public float RegenOfHp { get; private set; }
        public float CurrentMp { get; private set; }
        public float MaxMp { get; private set; }
        public float RegenOfMp { get; private set; }

        public int Experience { get; private set; }
        public int MaxStorageWeight { get; private set; }

        public List <AppliedModifier> ModifiersOnUnit { get; private set; }
        public HashSet <int> AvailableSpells { get; private set; }

        public int [] Charactiristics { get; private set; }
        public float [] Resists { get; private set; }
        public int LineageId { get; private set; }

        public bool HpRegenerationBlocked { get; private set; }
        public bool MpRegenerationBlocked { get; private set; }
        public bool MovementBlocked { get; private set; }
        public float UnblockableHpRegeneration { get; private set; }
        public float UnblockableMpRegeneration { get; private set; }

        public ISpell CurrentlyCasting { get; private set; }
        public Unit SpellTarget { get; set; }
        public float TimeFromCastingStart { get; private set; }
        public Unit LastDamager { get; private set; } = null;
        public IGameAi Ai { get; private set; }

        public Unit ()
        {
            Id = -1;
            Alive = true;
            LineageId = -1;
            CurrentHp = 0.00001f;
            Charactiristics = new int[(int) CharacteristicsEnum.Count];
            Resists = new float[(int) DamageTypesEnum.Count];

            ModifiersOnUnit = new List <AppliedModifier> ();
            AvailableSpells = new HashSet <int> ();
        }

        public void AddExperience (int experience)
        {
            Debug.Assert (experience >= 0);
            Experience += experience;
        }

        public void ApplyDamage (float damage, Unit damager, DamageTypesEnum type)
        {
            Debug.Assert (damage >= 0.0f);
            CurrentHp = Math.Max (CurrentHp - damage * (1 - Math.Min (Resists [(int) type], 1.0f)), 0.0f);
            LastDamager = damager;
            Ai.OnDamage (this, LastDamager);
        }

        public void UseMovementPoints (float points)
        {
            Debug.Assert (CurrentMp - points >= 0.0f);
            CurrentMp = Math.Max (CurrentMp - points, 0.0f);
        }

        public void Heal (float amount)
        {
            Debug.Assert (amount >= 0.0f);
            CurrentHp = Math.Min (CurrentHp + amount, MaxHp);
        }

        public void Rest (float amount)
        {
            Debug.Assert (amount >= 0.0f);
            CurrentMp = Math.Min (CurrentMp + amount, MaxMp);
        }

        public bool StartCastingSpell (ISpell spell)
        {
            if (spell != null && !AvailableSpells.Contains (spell.Id))
            {
                return false;
            }

            CurrentlyCasting = spell;
            TimeFromCastingStart = 0.0f;
            return true;
        }

        public bool CanCast (int level = 1, Item item = null)
        {
            return CurrentlyCasting != null && CurrentlyCasting.CanCast (this, level, item,
                       CurrentlyCasting.TargetRequired ? SpellTarget : null) &&
                   TimeFromCastingStart >=
                   CurrentlyCasting.BasicCastTime + CurrentlyCasting.CastTimeAdditionPerLevel * level;
        }

        public bool CastSpell (int level = 1, Item item = null)
        {
            if (CurrentlyCasting == null || !CanCast (level, item))
            {
                return false;
            }

            var parameter = new SpellCastParameter (item, level, SpellTarget);
            CurrentlyCasting.Cast (this, UnitsHubRef, parameter);

            CurrentlyCasting = null;
            TimeFromCastingStart = 0.0f;
            SpellTarget = null;
            return true;
        }

        public void ApplyLineage (int lineageIndex)
        {
            Lineage lineage;
            if (LineageId != -1)
            {
                lineage = LineagesContainerRef.LineagesList [LineageId];
                for (int i = 0; i < 5; i++)
                {
                    Charactiristics [i] -= lineage.CharcsChanges [i];
                }
            }

            LineageId = lineageIndex;
            lineage = LineagesContainerRef.LineagesList [LineageId];

            for (int i = 0; i < 5; i++)
            {
                Charactiristics [i] += lineage.CharcsChanges [i];
            }
        }

        public void ApplyModifier (int id, int level, float overrideTime = -1.0f, bool blockAddition = false,
            bool blockCancel = false)
        {
            var modifierType = CharacterModifiersContainerRef.Modifiers [id];
            var modifier = new AppliedModifier (id, overrideTime < 0 ? modifierType.TimeOfBuff * level : overrideTime,
                level);
            ModifiersOnUnit.Add (modifier);

            for (int i = 0; i < (int) CharacteristicsEnum.Count; i++)
            {
                Charactiristics [i] += modifierType.CharcsChanges [i] * level;
            }

            for (int i = 0; i < (int) DamageTypesEnum.Count; i++)
            {
                Resists [i] += modifierType.ResistsChanges [i] * level;
            }

            UnblockableHpRegeneration += modifierType.UnblockableHpRegeneration * level;
            UnblockableMpRegeneration += modifierType.UnblockableMpRegeneration * level;
            HpRegenerationBlocked |= modifierType.BlocksHpRegeneration;
            MpRegenerationBlocked |= modifierType.BlocksMpRegeneration;
            MovementBlocked |= modifierType.BlocksMovement;

            int modifierIndex = 0;
            while (!blockCancel && modifierIndex < ModifiersOnUnit.Count)
            {
                var anotherModifier = ModifiersOnUnit [modifierIndex];
                if (modifierType.BuffsToCancel.Contains (anotherModifier.Id))
                {
                    RemoveModifier (anotherModifier);
                    ModifiersOnUnit.RemoveAt (modifierIndex);
                }
                else
                {
                    modifierIndex++;
                }
            }

            if (!blockAddition)
            {
                foreach (var anotherId in modifierType.BuffsToApply)
                {
                    ApplyModifier (anotherId, level);
                }
            }

            RecalculateChildCharacteristics ();
        }

        public void DecreaseModifiers (int ofType, int level)
        {
            int modifierIndex = 0;
            while (modifierIndex < ModifiersOnUnit.Count && level > 0)
            {
                var modifier = ModifiersOnUnit [modifierIndex];
                if (modifier.Id == ofType)
                {
                    if (modifier.Level <= level)
                    {
                        level -= modifier.Level;
                        RemoveModifier (modifier);
                        ModifiersOnUnit.RemoveAt (modifierIndex);
                    }
                    else
                    {
                        RemoveModifier (modifier);
                        ModifiersOnUnit.RemoveAt (modifierIndex);
                        ApplyModifier (modifier.Id, modifier.Level - level, modifier.Time, true, true);
                        break;
                    }
                }
                else
                {
                    modifierIndex++;
                }
            }
        }

        public int GetCharacteristic (CharacteristicsEnum characteristicIn)
        {
            return Charactiristics [(int) characteristicIn];
        }

        public void SetCharactiristic (CharacteristicsEnum typeOfCharacteristicIn, int valueOfCharacteristicIn)
        {
            Charactiristics [(int) typeOfCharacteristicIn] += valueOfCharacteristicIn;
            RecalculateChildCharacteristics ();
        }

        public bool LearnSpell (int spellId)
        {
            if (AvailableSpells.Add (spellId))
            {
                EventsHub.Instance.SendGlobalEvent (EventSpellLearned,
                    new SpellLearnedOrForgottenEventData (this, spellId));
                return true;
            }

            return false;
        }

        public bool ForgetSpell (int spellId)
        {
            if (AvailableSpells.Remove (spellId))
            {
                EventsHub.Instance.SendGlobalEvent (EventSpellForgotten,
                    new SpellLearnedOrForgottenEventData (this, spellId));

                if (CurrentlyCasting.Id == spellId)
                {
                    StartCastingSpell (null);
                }

                return true;
            }

            return false;
        }

        public void Resurrect (int alignment, float percentsOfMaxHp)
        {
            Debug.Assert (!Alive);
            Debug.Assert (percentsOfMaxHp >= 0.0f);
            Debug.Assert (percentsOfMaxHp <= 1.0f);

            if (!Alive)
            {
                Alive = true;
                Alignment = alignment;
                CurrentHp = percentsOfMaxHp * MaxHp;

                foreach (var modifier in ModifiersOnUnit)
                {
                    RemoveModifier (modifier);
                }

                ModifiersOnUnit.RemoveAll (item => true);
                LastDamager = null;
            }
        }

        public void RecalculateChildCharacteristics ()
        {
            NearDamageBust = 0;
            MaxHp = 0;
            MaxMp = 0;
            RegenOfHp = 0;
            RegenOfMp = 0;
            MaxStorageWeight = 0;

            OnDistanceDamageBust = 0;
            for (int index = 0; index < (int) DamageTypesEnum.Count; index++)
            {
                Resists [index] = 0;
            }

            MagicDamageBust = 0;
            CriticalDamageChance = 0;
            CriticalResistChance = 0;

            int strength = GetCharacteristic (CharacteristicsEnum.Strength);
            if (strength < 1) strength = 1;

            int agility = GetCharacteristic (CharacteristicsEnum.Agility);
            if (agility < 1) agility = 1;

            int vitality = GetCharacteristic (CharacteristicsEnum.Vitality);
            if (vitality < 1) vitality = 1;

            int intelligence = GetCharacteristic (CharacteristicsEnum.Intelligence);
            if (intelligence < 1) intelligence = 1;

            int luck = GetCharacteristic (CharacteristicsEnum.Luck);
            if (luck < 1) luck = 1;

            //обработка силы            
            NearDamageBust += (float) (strength * 0.03);
            MaxHp += 5 * strength;
            MaxMp += 2 * strength;
            RegenOfHp += strength;
            MaxStorageWeight += 3 * strength;

            //ловкость
            OnDistanceDamageBust += (float) (0.03 * agility);
            MaxHp += 2 * agility;
            MaxMp += 5 * agility;
            RegenOfMp += agility;

            //выносливость
            for (int index = 0; index < (int) DamageTypesEnum.Count; index++)
            {
                Resists [index] += (float) (0.03 * vitality);
            }

            MaxHp += 4 * vitality;
            MaxMp += 4 * vitality;
            RegenOfHp += vitality;
            RegenOfMp += vitality;
            MaxStorageWeight += 3 * vitality;

            //разум
            MagicDamageBust += (float) (0.2 * intelligence);
            MaxMp += 3 * intelligence;
            RegenOfHp += intelligence;
            RegenOfMp += intelligence;

            //удачливость
            CriticalDamageChance += (float) (0.03 * luck);
            CriticalResistChance += (float) (0.03 * luck);
            RegenOfHp += luck;
            RegenOfMp += luck;
            MyStorage.MaxWeight = MaxStorageWeight;
        }

        // TODO: What about saving|loading hp and mp?
        public void LoadFromXML (XmlNode input, int replacedAlignment = -1)
        {
            Id = UnitsHubRef.RequestId (XmlHelper.GetIntAttribute (input, "Id"));
            Alignment = replacedAlignment == -1 ? XmlHelper.GetIntAttribute (input, "Alignment") : replacedAlignment;
            Name = input.Attributes ["Name"].InnerText;
            IsMan = XmlHelper.GetBoolAttribute (input, "IsMan");
            Experience = XmlHelper.GetIntAttribute (input, "Experience");

            string charsStringData = input.Attributes ["Characteristics"].InnerText;
            string [] charsSeparated = charsStringData.Split (' ').Select (tag => tag.Trim ())
                .Where (tag => !string.IsNullOrEmpty (tag)).ToArray ();

            for (int index = 0; index < charsSeparated.Length; index++)
            {
                Charactiristics [index] =
                    int.Parse (charsSeparated [index], NumberFormatInfo.InvariantInfo);
            }

            ModifiersOnUnit.Clear ();
            foreach (var modifier in XmlHelper.IterateChildren (input, "modifier"))
            {
                // TODO: Not a best way.
                ApplyModifier (XmlHelper.GetIntAttribute (modifier, "Id"),
                    XmlHelper.GetIntAttribute (modifier, "Level"));
            }

            string resistsStringData = input.Attributes ["Resists"].InnerText;
            string [] resistsSeparated = resistsStringData.Split (' ').Select (tag => tag.Trim ())
                .Where (tag => !string.IsNullOrEmpty (tag)).ToArray ();

            for (int index = 0; index < resistsSeparated.Length; index++)
            {
                Resists [index] =
                    float.Parse (resistsSeparated [index], NumberFormatInfo.InvariantInfo);
            }

            string availableSpellsStringData = input.Attributes ["AvailableSpells"].InnerText;
            string [] availableSpellsSeparated = availableSpellsStringData.Split (' ').Select (tag => tag.Trim ())
                .Where (tag => !string.IsNullOrEmpty (tag)).ToArray ();
            AvailableSpells.Clear ();

            foreach (var spellIdString in availableSpellsSeparated)
            {
                LearnSpell (int.Parse (spellIdString));
            }

            ApplyLineage (XmlHelper.GetIntAttribute (input, "LineageId"));
            MyStorage.LoadFromXML (ItemsRegistratorRef, XmlHelper.FirstChild (input, "storage"));
            MyEquipment.LoadFromXML (MyStorage, XmlHelper.FirstChild (input, "equipment"));
            Ai = GameAiList.Ais [input.Attributes ["Ai"].InnerText] ();
            RecalculateChildCharacteristics ();

            CurrentHp = MaxHp;
            CurrentMp = MaxMp;
        }

        public void SaveToXml (XmlElement output)
        {
            output.SetAttribute ("Id", Id.ToString (NumberFormatInfo.InvariantInfo));
            output.SetAttribute ("Alignment", Alignment.ToString (NumberFormatInfo.InvariantInfo));
            output.SetAttribute ("Name", Name);
            output.SetAttribute ("IsMan", IsMan.ToString ());
            output.SetAttribute ("Experience", Experience.ToString (NumberFormatInfo.InvariantInfo));

            foreach (var modifier in ModifiersOnUnit)
            {
                CharacterModifier modifierType = CharacterModifiersContainerRef.Modifiers [modifier.Id];
                for (int i = 0; i < (int) CharacteristicsEnum.Count; i++)
                {
                    Charactiristics [i] -= modifierType.CharcsChanges [i] * modifier.Level;
                }
            }

            var stringBuilder = new StringBuilder ();
            foreach (var characteristic in Charactiristics)
            {
                stringBuilder.Append (characteristic).Append (' ');
            }

            output.SetAttribute ("Characteristics", stringBuilder.ToString ());
            stringBuilder.Clear ();

            foreach (var modifier in ModifiersOnUnit)
            {
                CharacterModifier modifierType = CharacterModifiersContainerRef.Modifiers [modifier.Id];
                for (int i = 0; i < (int) CharacteristicsEnum.Count; i++)
                {
                    Charactiristics [i] += modifierType.CharcsChanges [i] * modifier.Level;
                }
            }

            foreach (var modifier in ModifiersOnUnit)
            {
                var modifierElement = output.OwnerDocument.CreateElement ("modifier");
                modifierElement.SetAttribute ("Id", modifier.Id.ToString (NumberFormatInfo.InvariantInfo));
                modifierElement.SetAttribute ("Level", modifier.Level.ToString (NumberFormatInfo.InvariantInfo));
                output.AppendChild (modifierElement);
            }

            foreach (var resist in Resists)
            {
                stringBuilder.Append (resist).Append (' ');
            }

            output.SetAttribute ("Resists", stringBuilder.ToString ());
            stringBuilder.Clear ();

            foreach (var availableSpell in AvailableSpells)
            {
                stringBuilder.Append (availableSpell).Append (' ');
            }

            output.SetAttribute ("AvailableSpells", stringBuilder.ToString ());
            stringBuilder.Clear ();

            output.SetAttribute ("LineageId", LineageId.ToString (NumberFormatInfo.InvariantInfo));
            var storageElement = output.OwnerDocument.CreateElement ("storage");
            MyStorage.SaveToXml (storageElement);
            output.AppendChild (storageElement);

            var equipmentElement = output.OwnerDocument.CreateElement ("equipment");
            MyEquipment.SaveToXml (equipmentElement);
            output.AppendChild (equipmentElement);
            
            // TODO: Save ai type.
        }

        private void RemoveModifier (AppliedModifier modifier)
        {
            CharacterModifier modifierType = CharacterModifiersContainerRef.Modifiers [modifier.Id];

            for (int i = 0; i < (int) CharacteristicsEnum.Count; i++)
            {
                Charactiristics [i] -= modifierType.CharcsChanges [i] * modifier.Level;
            }

            for (int i = 0; i < (int) DamageTypesEnum.Count; i++)
            {
                Resists [i] -= modifierType.ResistsChanges [i] * modifier.Level;
            }

            UnblockableHpRegeneration -= modifierType.UnblockableHpRegeneration * modifier.Level;
            UnblockableMpRegeneration -= modifierType.UnblockableMpRegeneration * modifier.Level;

            HpRegenerationBlocked = false;
            MpRegenerationBlocked = false;
            MovementBlocked = false;

            foreach (var anotherModifier in ModifiersOnUnit)
            {
                if (anotherModifier != modifier)
                {
                    HpRegenerationBlocked |=
                        CharacterModifiersContainerRef.Modifiers [anotherModifier.Id].BlocksHpRegeneration;

                    MpRegenerationBlocked |=
                        CharacterModifiersContainerRef.Modifiers [anotherModifier.Id].BlocksMpRegeneration;

                    MovementBlocked |= CharacterModifiersContainerRef.Modifiers [anotherModifier.Id].BlocksMovement;
                }
            }

            RecalculateChildCharacteristics ();
        }

        private new void Start ()
        {
            GameEngineCoreUtils.GetCoreInstances (
                out UnitsHubRef, out ItemsRegistratorRef, out ItemTypesContainerRef,
                out SpellsContainerRef, out CharacterModifiersContainerRef, out LineagesContainerRef);

            Id = UnitsHubRef.RequestId (Id);
            MyStorage = new Storage (ItemTypesContainerRef);
            MyEquipment = new Equipment (ItemTypesContainerRef);
            
            EventsHub.Instance.Subscribe (this, Storage.EventItemAdded);
            EventsHub.Instance.Subscribe (this, Storage.EventItemRemoved);

            RecalculateChildCharacteristics ();
            base.Start ();
        }

        private new void OnDestroy ()
        {
            base.OnDestroy ();
            EventsHub.Instance.Unsubscribe (this, Storage.EventItemAdded);
            EventsHub.Instance.Unsubscribe (this, Storage.EventItemRemoved);
        }

        private void Update ()
        {
            if (CurrentHp <= 0.0f && Alive)
            {
                Alive = false;
                OnDeath ();

                Ai?.OnDie (this);
            }

            if (!Alive)
            {
                return;
            }

            Ai?.Process (this);

            MyStorage.UpdateItems (Time.deltaTime);
            if (CurrentlyCasting != null && (!CurrentlyCasting.MovementRequired || !MovementBlocked))
            {
                TimeFromCastingStart += Time.deltaTime;
            }

            if (!HpRegenerationBlocked)
            {
                CurrentHp += RegenOfHp * Time.deltaTime;
            }

            CurrentHp += UnblockableHpRegeneration * Time.deltaTime;
            if (CurrentHp > MaxHp) CurrentHp = MaxHp;

            if (!MpRegenerationBlocked)
            {
                CurrentMp += RegenOfMp * Time.deltaTime;
            }

            CurrentMp += UnblockableMpRegeneration * Time.deltaTime;
            if (CurrentMp > MaxMp) CurrentMp = MaxMp;

            int modifierIndex = 0;
            while (modifierIndex < ModifiersOnUnit.Count)
            {
                var modifier = ModifiersOnUnit [modifierIndex];
                modifier.Time -= Time.deltaTime;

                if (modifier.Time <= 0.0f)
                {
                    RemoveModifier (modifier);
                    ModifiersOnUnit.RemoveAt (modifierIndex);
                }
                else
                {
                    modifierIndex++;
                }
            }
        }

        private void OnDeath ()
        {
            var random = new Random ();
            foreach (var item in MyStorage.Items)
            {
                ItemsRegistratorRef.SpawnItemAsObject (ItemTypesContainerRef, item,
                    transform.position + new Vector3 ((float) (random.NextDouble () * 2.0f), 0.0f,
                        (float) random.NextDouble () * 2.0f));
            }

            MyStorage.Clear ();
            LastDamager.AddExperience (100);
        }

        private void ItemAdded (object parameter)
        {
            var data = parameter as Storage.ItemAddedOrRemovedEventData;
            if (data.EventStorage == MyStorage)
            {
                var type = ItemTypesContainerRef.ItemTypes [MyStorage [data.ItemId].ItemTypeId];
                foreach (var spellId in type.OpensSpells)
                {
                    LearnSpell (spellId);
                }
            }
        }

        private void ItemRemoved (object parameter)
        {
            var data = parameter as Storage.ItemAddedOrRemovedEventData;
            if (data.EventStorage == MyStorage)
            {
                var type = ItemTypesContainerRef.ItemTypes [MyStorage [data.ItemId].ItemTypeId];
                var found = new HashSet <int> ();
                
                foreach (var item in MyStorage.Items)
                {
                    if (item.Id == data.ItemId)
                    {
                        continue;
                    }
                    
                    var itemType = ItemTypesContainerRef.ItemTypes [item.ItemTypeId];
                    foreach (var spellId in itemType.OpensSpells)
                    {
                        found.Add (spellId);
                    }
                }
                
                foreach (var spellId in type.OpensSpells)
                {
                    if (!found.Contains (spellId))
                    {
                        ForgetSpell (spellId);
                    }
                }
            }
        }
    }
}