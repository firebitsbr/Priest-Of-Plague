﻿using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using PriestOfPlague.Source.Items;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace PriestOfPlague.Source.Ingame
{
    public enum PlayerCurrentActionType
    {
        Walk = 0,
        Count
    }

    public class PlayerController : UnitAnimator
    {
        public PlayerCurrentActionType PlayerCurrentAction { get; private set; } = PlayerCurrentActionType.Walk;
        public float NavigationAccuracy = 1.0f;
        private Camera _playerCamera;

        public void MouseClick ()
        {
            if (PlayerCurrentAction == PlayerCurrentActionType.Walk)
            {
                var ray = _playerCamera.ScreenPointToRay (Input.mousePosition);
                RaycastHit raycastHit;
                if (Physics.Raycast (ray, out raycastHit))
                {
                    if (_unit.CurrentlyCasting != null && _unit.CurrentlyCasting.TargetRequired)
                    {
                        var selectedUnit = raycastHit.collider.gameObject.GetComponent <Unit.Unit> ();
                        if (selectedUnit != null)
                        {
                            _unit.SpellTarget = selectedUnit;
                            return;
                        }
                    }

                    if ((raycastHit.point - _unit.transform.position).magnitude <
                        GetComponent <CapsuleCollider> ().radius * 2)
                    {
                        var container = raycastHit.collider.gameObject.GetComponent <SpawnedItemContainer> ();
                        if (container != null)
                        {
                            if (_unit.MyStorage.AddItem (container.SpawnedItem))
                            {
                                Destroy (container.gameObject);
                            }

                            return;
                        }
                    }

                    NavMeshHit navMeshHit;
                    if (NavMesh.SamplePosition (raycastHit.point, out navMeshHit, NavigationAccuracy,
                        _navMeshAgent.areaMask))
                    {
                        _navMeshAgent.SetDestination (navMeshHit.position);
                        _unit.StartCastingSpell (null);
                    }
                }
            }
        }

        public void SelectWalkAction ()
        {
            PlayerCurrentAction = PlayerCurrentActionType.Walk;
            _unit.StartCastingSpell (null);
        }

        private new IEnumerator Start ()
        {
            yield return base.Start ();
            _playerCamera = GetComponentInChildren <Camera> ();
        }

        private new void Update ()
        {
            base.Update ();
        }
    }
}