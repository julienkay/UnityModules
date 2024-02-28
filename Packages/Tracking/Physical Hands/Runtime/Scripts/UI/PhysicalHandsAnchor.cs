/******************************************************************************
 * Copyright (C) Ultraleap, Inc. 2011-2024.                                   *
 *                                                                            *
 * Use subject to the terms of the Apache License 2.0 available at            *
 * http://www.apache.org/licenses/LICENSE-2.0, or another agreement           *
 * between Ultraleap and you, your company or other organization.             *
 ******************************************************************************/


using System;
using System.Collections.Generic;
using UnityEngine;
using Leap.Unity.Attachments;


namespace Leap.Unity.PhysicalHands
{

    public class PhysicalHandsAnchor : MonoBehaviour
    {

        private static HashSet<PhysicalHandsAnchor> _allAnchors;
        public static HashSet<PhysicalHandsAnchor> allAnchors
        {
            get
            {
                if (_allAnchors == null)
                {
                    _allAnchors = new HashSet<PhysicalHandsAnchor>();
                }
                return _allAnchors;
            }
        }

        [Tooltip("Should this anchor allow multiple objects to be attached to it at the same time? "
               + "This property is enforced by AnchorGroups and PhysicalHandsAnchorableBehaviours.")]
        public bool allowMultipleObjects = false;

        [Tooltip("Should this anchor attempt to enable and disable the GameObjects of attached "
               + "PhysicalHandsAnchorableBehaviours when its own active state changes? If this setting is enabled, "
               + "the Anchor will deactivate the attached objects when its own GameObject is deactivated "
               + "or if its script is disabled, and similarly for becoming active or enabled.")]
        public bool matchActiveStateWithAttachedObjects = false;

        private HashSet<PhysicalHandsAnchorGroup> _groups = new HashSet<PhysicalHandsAnchorGroup>();
        public HashSet<PhysicalHandsAnchorGroup> groups { get { return _groups; } }

        private HashSet<PhysicalHandsAnchorableBehaviour> _preferringAnchorables = new HashSet<PhysicalHandsAnchorableBehaviour>();

        private HashSet<PhysicalHandsAnchorableBehaviour> _anchoredObjects = new HashSet<PhysicalHandsAnchorableBehaviour>();

        /// <summary>
        /// returns ChiralitySelection.NONE if no attachemt hand found in parents
        /// </summary>
        internal ChiralitySelection AttahedHandChirality
        {
            get
            {
                return GetChiralityOfAttachedHand();
            }
            private set
            {
                _attachedHandChirality = value;
            }
        }
        private ChiralitySelection _attachedHandChirality = ChiralitySelection.NONE;
        /// <summary>
        /// Gets the set of PhysicalHandsAnchorableBehaviours currently attached to this anchor.
        /// </summary>
        public HashSet<PhysicalHandsAnchorableBehaviour> anchoredObjects { get { return _anchoredObjects; } }

        public bool isPreferred { get { return _preferringAnchorables.Count > 0; } }

        public bool hasAnchoredObjects { get { return _anchoredObjects.Count > 0; } }

        #region Events

        /// <summary>
        /// Called as soon as any anchorable objects prefer this anchor if they were to try to
        /// attach to an anchor.
        /// </summary>
        public Action OnAnchorPreferred = () => { };

        /// <summary>
        /// Called when no anchorable objects prefer this anchor any more.
        /// </summary>
        public Action OnAnchorNotPreferred = () => { };

        /// <summary>
        /// Called every Update() that an PhysicalHandsAnchorableBehaviour prefers this anchor.
        /// </summary>
        public Action WhileAnchorPreferred = () => { };

        /// <summary>
        /// Called as soon as any anchorables become attached to this anchor.
        /// </summary>
        public Action OnAnchorablesAttached = () => { };

        /// <summary>
        /// Called when there are no anchorables attached to this anchor.
        /// </summary>
        public Action OnNoAnchorablesAttached = () => { };

        /// <summary>
        /// Called every Update() that one or more PhysicalHandsAnchorableBehaviours is attached to this anchor.
        /// </summary>
        public Action WhileAnchorablesAttached = () => { };

        #endregion

        void Awake()
        {
            allAnchors.Add(this);
        }

        void OnEnable()
        {
            if (matchActiveStateWithAttachedObjects)
            {
                foreach (var anchObj in _anchoredObjects)
                {
                    anchObj.gameObject.SetActive(true);
                }
            }
        }

        void Start()
        {
            initUnityEvents();
            _attachedHandChirality = GetChiralityOfAttachedHand();
        }

        void Update()
        {
            updateAnchorCallbacks();
        }

        /// <summary>
        /// Gets the chirality of the hand which this object is attached to if any
        /// </summary>
        /// <returns>Chirality of the attached hand</returns>
        internal ChiralitySelection GetChiralityOfAttachedHand()
        {
            AttachmentHand handObject = transform.root.GetComponentInChildren<AttachmentHand>();

            if (handObject != null)
            {
                return (ChiralitySelection)((int)handObject.chirality);
            }
            else
            {
                return ChiralitySelection.NONE;
            }
            
        }

        void OnDisable()
        {
            if (matchActiveStateWithAttachedObjects)
            {
                foreach (var anchObj in _anchoredObjects)
                {
                    anchObj.gameObject.SetActive(false);
                }
            }
        }

        void OnDestroy()
        {
            var tempGroups = new HashSet<PhysicalHandsAnchorGroup>(groups);
            foreach (var group in tempGroups)
            {
                group.Remove(this);
            }
            groups.Clear();

            allAnchors.Remove(this);
        }

        #region Anchor Callbacks

        public void NotifyAttached(PhysicalHandsAnchorableBehaviour anchObj)
        {
            _anchoredObjects.Add(anchObj);

            if (_anchoredObjects.Count == 1)
            {
                OnAnchorablesAttached();
            }
        }

        public void NotifyDetached(PhysicalHandsAnchorableBehaviour anchObj)
        {
            _anchoredObjects.Remove(anchObj);

            if (_anchoredObjects.Count == 0)
            {
                OnNoAnchorablesAttached();
            }
        }

        private void updateAnchorCallbacks()
        {
            WhileAnchorPreferred();
            WhileAnchorablesAttached();
        }

        public void NotifyAnchorPreference(PhysicalHandsAnchorableBehaviour anchObj)
        {
            _preferringAnchorables.Add(anchObj);

            if (_preferringAnchorables.Count == 1)
            {
                OnAnchorPreferred();
            }
        }

        public void NotifyEndAnchorPreference(PhysicalHandsAnchorableBehaviour anchObj)
        {
            _preferringAnchorables.Remove(anchObj);

            if (_preferringAnchorables.Count == 0)
            {
                OnAnchorNotPreferred();
            }
        }

        #endregion

        #region Gizmos

        public static Color AnchorGizmoColor = new Color(0.6F, 0.2F, 0.8F);

        void OnDrawGizmosSelected()
        {
            Matrix4x4 origMatrix = Gizmos.matrix;
            Gizmos.matrix = this.transform.localToWorldMatrix;
            Gizmos.color = AnchorGizmoColor;
            float radius = 0.015F;

            drawWireSphereGizmo(Vector3.zero, radius);

            drawSphereCirclesGizmo(5, Vector3.zero, radius, Vector3.forward);

            Gizmos.matrix = origMatrix;
        }

        private static Vector3[] worldDirs = new Vector3[] { Vector3.right, Vector3.up, Vector3.forward };

        private void drawWireSphereGizmo(Vector3 pos, float radius)
        {
            foreach (var dir in worldDirs)
            {
                if (dir == Vector3.forward)
                {
                    continue;
                }
                Leap.Unity.Utils.DrawCircle(pos, dir, radius, AnchorGizmoColor, quality: 24, depthTest: true);
            }
        }

        private void drawSphereCirclesGizmo(int numCircles, Vector3 pos, float radius, Vector3 poleDir)
        {
            float dTheta = 180F / numCircles;
            float halfTheta = dTheta / 2F;

            for (int i = 0; i < numCircles; i++)
            {
                float curTheta = (dTheta * i) + halfTheta;
                Leap.Unity.Utils.DrawCircle(pos + poleDir * Mathf.Cos(curTheta * Mathf.Deg2Rad) * radius, poleDir, Mathf.Sin(curTheta * Mathf.Deg2Rad) * radius, AnchorGizmoColor, quality: 16, depthTest: true);
            }
        }

        #endregion

        #region Unity Events (Internal)

        [SerializeField]
        private EnumEventTable _eventTable = null;

        public enum EventType
        {
            OnAnchorPreferred = 100,
            OnAnchorNotPreferred = 110,
            WhileAnchorPreferred = 120,
            OnAnchorablesAttached = 130,
            OnNoAnchorablesAttached = 140,
            WhileAnchorablesAttached = 150,
        }

        private void initUnityEvents()
        {
            setupCallback(ref OnAnchorPreferred, EventType.OnAnchorPreferred);
            setupCallback(ref OnAnchorNotPreferred, EventType.OnAnchorNotPreferred);
            setupCallback(ref WhileAnchorPreferred, EventType.WhileAnchorPreferred);
            setupCallback(ref OnAnchorablesAttached, EventType.OnAnchorablesAttached);
            setupCallback(ref OnNoAnchorablesAttached, EventType.OnNoAnchorablesAttached);
            setupCallback(ref WhileAnchorablesAttached, EventType.WhileAnchorablesAttached);
        }

        private void setupCallback(ref Action action, EventType type)
        {
            action += () => _eventTable.Invoke((int)type);
        }

        #endregion

    }

}