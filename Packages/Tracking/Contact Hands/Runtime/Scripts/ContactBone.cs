using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Leap.Unity.ContactHands
{
    public abstract class ContactBone : MonoBehaviour
    {
        #region Bone Parameters
        internal int finger, joint;
        internal bool isPalm = false;
        public int Finger => finger;
        public int Joint => joint;
        public bool IsPalm => isPalm;

        internal float width = 0f, length = 0f, palmThickness = 0f;
        internal Vector3 tipPosition = Vector3.zero;
        internal Vector3 wristPosition = Vector3.zero;
        internal Vector3 center = Vector3.zero;

        internal Rigidbody rigid;
        internal ArticulationBody articulation;

        internal CapsuleCollider boneCollider;
        internal BoxCollider palmCollider;
        protected Collider Collider { get { return IsPalm ? palmCollider : boneCollider; } }

        internal ContactHand contactHand;
        internal ContactParent contactParent => contactHand.contactParent;
        #endregion

        #region Physics Data

        #endregion

        #region Interaction Data
        private static float SAFETY_CLOSE_DISTANCE = 0.005f;

        public class ClosestObjectDirection
        {
            /// <summary>
            /// The closest position on the hovering bone
            /// </summary>
            public Vector3 bonePos = Vector3.zero;
            /// <summary>
            /// The direction from the closest pos on the bone to the closest pos on the collider
            /// </summary>
            public Vector3 direction = Vector3.zero;
            /// <summary>
            /// Distance from the raw "position" of the bone
            /// </summary>
            public float distance = 0f;
        }

        private Dictionary<Rigidbody, HashSet<Collider>> _colliderObjects = new Dictionary<Rigidbody, HashSet<Collider>>();
        private Dictionary<Rigidbody, float> _objectDistances = new Dictionary<Rigidbody, float>();

        private Dictionary<Rigidbody, HashSet<Collider>> _hoverObjects = new Dictionary<Rigidbody, HashSet<Collider>>();
        public Dictionary<Rigidbody, HashSet<Collider>> HoverObjects => _hoverObjects;

        [field: SerializeField, Tooltip("Is the bone hovering an object? The hover distances are set in the Physics Provider.")]
        public bool IsBoneHovering { get; private set; } = false;
        public bool IsBoneHoveringRigid(Rigidbody rigid)
        {
            return _hoverObjects.TryGetValue(rigid, out HashSet<Collider> result);
        }

        private Dictionary<Rigidbody, HashSet<Collider>> _contactObjects = new Dictionary<Rigidbody, HashSet<Collider>>();
        public Dictionary<Rigidbody, HashSet<Collider>> ContactObjects => _contactObjects;
        [field: SerializeField, Tooltip("Is the bone contacting with an object? The contact distances are set in the Physics Provider.")]
        public bool IsBoneContacting { get; private set; } = false;

        public bool IsBoneContactingRigid(Rigidbody rigid)
        {
            return _contactObjects.TryGetValue(rigid, out HashSet<Collider> result);
        }

        private HashSet<Rigidbody> _grabObjects = new HashSet<Rigidbody>();

        ///<summary>
        /// Dictionary of dictionaries of the directions from this bone to a hovered object's colliders
        ///</summary>
        public Dictionary<Rigidbody, Dictionary<Collider, ClosestObjectDirection>> HoverDirections => _objectDirections;
        private Dictionary<Rigidbody, Dictionary<Collider, ClosestObjectDirection>> _objectDirections = new Dictionary<Rigidbody, Dictionary<Collider, ClosestObjectDirection>>();

        ///<summary>
        /// Dictionary of dictionaries of the directions from this bone to a grabbable object's colliders
        ///</summary>
        public Dictionary<Rigidbody, Dictionary<Collider, ClosestObjectDirection>> GrabbableDirections => _grabbableDirections;
        private Dictionary<Rigidbody, Dictionary<Collider, ClosestObjectDirection>> _grabbableDirections = new Dictionary<Rigidbody, Dictionary<Collider, ClosestObjectDirection>>();

        /// <summary>
        /// Objects that *can* be grabbed, not ones that are
        /// </summary>
        public HashSet<Rigidbody> GrabbableObjects => _grabObjects;
        [field: SerializeField, Tooltip("Is the bone ready to grab an object that sits in front of it?")]
        public bool IsBoneReadyToGrab { get; private set; } = false;

        private HashSet<Rigidbody> _grabbedObjects = new HashSet<Rigidbody>();
        /// <summary>
        /// Objects that are currently grabbed according to the grasp helper
        /// </summary>
        public HashSet<Rigidbody> GrabbedObjects => _grabbedObjects;
        /// <summary>
        /// Whether the grasp helper has reported that this bone is grabbing.
        /// If a bone further towards the tip is reported as grabbing, then this bone will also be.
        /// </summary>
        [field: SerializeField, Tooltip("Is the bone currently being used to grab an object via a grasp helper? If a bone further towards the tip is reported as grabbing, then this bone will also be.")]
        public bool IsBoneGrabbing { get; private set; } = false;

        /// <summary>
        /// Will report float.MaxValue if IsHovering is false
        /// </summary>
        public float ObjectDistance => _objectDistance;
        [SerializeField, Tooltip("The distance between the edge of the bone collider and the nearest object. Will report float.MaxValue if IsHovering is false.")]
        private float _objectDistance = float.MaxValue;
        #endregion

        private Vector3 _debugA, _debugB;

        internal abstract void UpdatePalmBone(Hand hand);
        internal abstract void UpdateBone(Bone prevBone, Bone bone);

        internal abstract void PostFixedUpdateBone();


        #region Interaction Functions
        internal void ProcessColliderQueue(Collider[] colliderCache, int count)
        {
            foreach (var key in _colliderObjects.Keys)
            {
                // Remove items that aren't hovered by the hand
                _colliderObjects[key].RemoveWhere(c => RemoveColliderQueue(c, colliderCache, count));
            }

            // Update the joint collider positions for cached checks, palm uses the collider directly
            if (!IsPalm)
            {
                boneCollider.ToWorldSpaceCapsule(out tipPosition, out var temp, out width);
            }

            UpdateObjectDistances(colliderCache, count);

            ClearOldObjects();

            // Remove the grabbed objects where they're no longer contacting
            _grabObjects.RemoveWhere(RemoveGrabQueue);

            foreach (var key in _contactObjects.Keys)
            {
                if (IsObjectGrabbable(key))
                {
                    // add to grabbable rb dicts
                    _grabObjects.Add(key);

                    if (_grabbableDirections.ContainsKey(key))
                    {
                        _grabbableDirections[key] = _objectDirections[key];
                    }
                    else
                    {
                        _grabbableDirections.Add(key, _objectDirections[key]);
                    }
                }
                else
                {
                    _grabObjects.Remove(key);
                    _grabbableDirections.Remove(key);
                }
            }

            IsBoneHovering = _hoverObjects.Count > 0;
            IsBoneContacting = _contactObjects.Count > 0;
            IsBoneReadyToGrab = _grabObjects.Count > 0;
        }

        private bool RemoveColliderQueue(Collider collider, Collider[] colliderCache, int count)
        {
            if (colliderCache.ContainsRange(collider, count))
            {
                return false;
            }

            if (collider.attachedRigidbody != null)
            {
                if (_objectDirections.ContainsKey(collider.attachedRigidbody))
                {
                    _objectDirections[collider.attachedRigidbody].Remove(collider);
                }
                if (_hoverObjects.ContainsKey(collider.attachedRigidbody))
                {
                    _hoverObjects[collider.attachedRigidbody].Remove(collider);
                }
                if (_contactObjects.ContainsKey(collider.attachedRigidbody))
                {
                    _contactObjects[collider.attachedRigidbody].Remove(collider);
                }
            }

            return true;
        }

        private bool RemoveGrabQueue(Rigidbody rigid)
        {
            if (_contactObjects.TryGetValue(rigid, out var temp))
            {
                return false;
            }
            return true;
        }

        // TODO: Palm logic needs to be done separately
        private void UpdateObjectDistances(Collider[] colliderCache, int count)
        {
            _objectDistance = float.MaxValue;

            float distance, singleObjectDistance, boneDistance;
            Vector3 colliderPos, bonePos, midPoint, direction;
            bool hover;

            Collider collider;
            for (int i = 0; i < count; i++)
            {
                collider = colliderCache[i];

                colliderPos = collider.ClosestPoint(Collider.transform.position);

                if (IsPalm)
                {
                    // Do palm logic
                    bonePos = Collider.ClosestPoint(collider.transform.position);
                    midPoint = colliderPos + (bonePos - colliderPos) / 2f;

                    colliderPos = collider.ClosestPoint(midPoint);
                    bonePos = Collider.ClosestPoint(midPoint);
                }
                else
                {
                    // Do joint logic
                    bonePos = ContactUtils.GetClosestPointOnFiniteLine(collider.transform.position, transform.position, tipPosition);
                    // We need to extrude the line to get the bone's edge
                    midPoint = colliderPos + (bonePos + ((bonePos - colliderPos).normalized * width) - colliderPos) / 2f;

                    colliderPos = collider.ClosestPoint(midPoint);
                    bonePos = ContactUtils.GetClosestPointOnFiniteLine(midPoint, transform.position, tipPosition);
                }

                distance = Vector3.Distance(colliderPos, bonePos);
                direction = (colliderPos - bonePos).normalized;

                if (IsPalm)
                {
                    boneDistance = distance;
                }
                else
                {
                    boneDistance = Mathf.Clamp(distance - width, 0, float.MaxValue);
                }

                hover = boneDistance <= contactParent.contactManager.HoverDistance;

                if (hover)
                {
                    if (!_objectDirections.ContainsKey(collider.attachedRigidbody))
                    {
                        _objectDirections.Add(collider.attachedRigidbody, new Dictionary<Collider, ClosestObjectDirection>());
                        _objectDirections[collider.attachedRigidbody].Add(collider, new ClosestObjectDirection());
                    }

                    if (_objectDirections[collider.attachedRigidbody].ContainsKey(collider))
                    {
                        _objectDirections[collider.attachedRigidbody][collider].direction = direction;
                        _objectDirections[collider.attachedRigidbody][collider].bonePos = bonePos + (IsPalm ? Vector3.zero : (direction * width));
                        _objectDirections[collider.attachedRigidbody][collider].distance = distance;
                    }
                    else
                    {
                        _objectDirections[collider.attachedRigidbody].Add(
                            collider,
                            new ClosestObjectDirection()
                            {
                                bonePos = bonePos + (IsPalm ? Vector3.zero : (direction * width)),
                                direction = direction,
                                distance = distance
                            }
                        );
                    }
                }
                else
                {
                    if (_objectDirections.ContainsKey(collider.attachedRigidbody))
                    {
                        _objectDirections[collider.attachedRigidbody].Remove(collider);
                    }
                }

                // Hover
                UpdateHoverCollider(collider.attachedRigidbody, collider, hover);

                // Contact
                UpdateContactCollider(collider.attachedRigidbody, collider, boneDistance <= contactParent.contactManager.ContactDistance);

                // Safety
                UpdateSafetyCollider(collider, boneDistance == 0, boneDistance <= SAFETY_CLOSE_DISTANCE);
            }

            foreach (var colliderPairs in _objectDirections)
            {
                singleObjectDistance = float.MaxValue;
                foreach (var col in colliderPairs.Value)
                {
                    if (col.Value.distance < singleObjectDistance)
                    {
                        singleObjectDistance = col.Value.distance;
                        if (singleObjectDistance < _objectDistance)
                        {
                            _objectDistance = singleObjectDistance;
                            if (IsPalm)
                            {
                                _debugA = col.Value.bonePos;
                                _debugB = col.Value.bonePos + (col.Value.direction * (col.Value.distance - width));
                            }
                        }
                    }
                }

                singleObjectDistance -= IsPalm ? 0 : width;

                // Store the distance for the object
                if (_objectDistances.ContainsKey(colliderPairs.Key))
                {
                    _objectDistances[colliderPairs.Key] = singleObjectDistance;
                }
                else
                {
                    _objectDistances.Add(colliderPairs.Key, singleObjectDistance);
                }
            }
            _objectDistance -= IsPalm ? 0 : width;
        }

        // TODO: add events
        private void UpdateHoverCollider(Rigidbody rigid, Collider collider, bool hover)
        {
            if (hover)
            {
                contactHand.isHovering = true;
            }

            if (_hoverObjects.ContainsKey(rigid))
            {
                if (_hoverObjects[rigid].Contains(collider))
                {
                    if (!hover)
                    {
                        _hoverObjects[rigid].Remove(collider);
                    }
                }
                else
                {
                    if (hover)
                    {
                        _hoverObjects[rigid].Add(collider);
                    }
                }
            }
            else
            {
                if (hover)
                {
                    _hoverObjects.Add(rigid, new HashSet<Collider>() { collider });
                }
            }
        }

        // TODO: add events
        private void UpdateContactCollider(Rigidbody rigid, Collider collider, bool contact)
        {
            if (contact)
            {
                contactHand.isContacting = true;
            }

            if (_contactObjects.ContainsKey(rigid))
            {
                if (_contactObjects[rigid].Contains(collider))
                {
                    if (!contact)
                    {
                        _contactObjects[rigid].Remove(collider);
                    }
                }
                else
                {
                    if (contact)
                    {
                        _contactObjects[rigid].Add(collider);
                    }
                }
            }
            else
            {
                if (contact)
                {
                    _contactObjects.Add(rigid, new HashSet<Collider>() { collider });
                }
            }
        }

        private void UpdateSafetyCollider(Collider collider, bool intersect, bool closeToObject)
        {
            if (WillColliderAffectBone(collider))
            {
                if (closeToObject)
                {
                    contactHand.isCloseToObject = true;
                }
                if (intersect)
                {
                    contactHand.isIntersecting = true;
                }
            }
        }

        private void ClearOldObjects()
        {
            // Remove empty entries
            var badKeys = _hoverObjects.Where(pair => pair.Value.Count == 0)
                        .Select(pair => pair.Key)
                        .ToList();
            foreach (var oldRigid in badKeys)
            {
                _hoverObjects.Remove(oldRigid);
                _objectDirections.Remove(oldRigid);
                _objectDistances.Remove(oldRigid);

                //if (oldRigid != null && oldRigid.TryGetComponent<IPhysicsBoneHover>(out var physicsHandGrab))
                //{
                //    physicsHandGrab.OnBoneHoverExit(this);
                //}
            }

            // Remove empty entries
            badKeys = _contactObjects.Where(pair => pair.Value.Count == 0)
                        .Select(pair => pair.Key)
                        .ToList();
            foreach (var oldRigid in badKeys)
            {
                _contactObjects.Remove(oldRigid);
                //if (oldRigid != null && oldRigid.TryGetComponent<IPhysicsBoneContact>(out var physicsHandGrab))
                //{
                //    physicsHandGrab.OnBoneContactExit(this);
                //}
            }
        }

        private bool IsObjectGrabbable(Rigidbody rigidbody)
        {
            if (_objectDirections.TryGetValue(rigidbody, out var hoveredColliders))
            {
                Vector3 bonePosCenter, closestPoint, boneCenterToColliderDirection, jointDirection;
                foreach (var hoveredColliderDirection in hoveredColliders)
                {
                    bonePosCenter = transform.TransformPoint(0, 0, transform.InverseTransformPoint(hoveredColliderDirection.Value.bonePos).z);

                    bool boneInsideCollider = hoveredColliderDirection.Key.IsSphereWithinCollider(bonePosCenter, width);
                    if (boneInsideCollider)
                    {
                        return true;
                    }

                    closestPoint = hoveredColliderDirection.Key.ClosestPoint(bonePosCenter);
                    boneCenterToColliderDirection = (closestPoint - bonePosCenter).normalized;

                    if (joint == 2)
                    {
                        // Rotate the direction forward if the contact is closer to the tip
                        jointDirection = Quaternion.AngleAxis(Mathf.InverseLerp(length, 0, Vector3.Distance(bonePosCenter, tipPosition)) * 45f, -transform.right) * -transform.up;
                    }
                    else
                    {
                        jointDirection = -transform.up;
                    }

                    switch (Finger)
                    {
                        case 0:
                            // Point the thumb closer to the index
                            jointDirection = Quaternion.AngleAxis(contactHand.handedness == Chirality.Left ? -45f : 45f, transform.forward) * jointDirection;
                            break;
                        case 1:
                            // Point the index closer to the thumb
                            jointDirection = Quaternion.AngleAxis(contactHand.handedness == Chirality.Left ? 25f : -25f, transform.forward) * jointDirection;
                            break;
                    }

                    if (Vector3.Dot(boneCenterToColliderDirection, jointDirection) > (Finger == 0 ? 0.04f : 0.18f))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal void AddGrabbing(Rigidbody rigid)
        {
            _grabbedObjects.Add(rigid);
            IsBoneGrabbing = _grabbedObjects.Count > 0;
        }

        internal void RemoveGrabbing(Rigidbody rigid)
        {
            _grabbedObjects.Remove(rigid);
            IsBoneGrabbing = _grabbedObjects.Count > 0;
        }

        private bool WillColliderAffectBone(Collider collider)
        {
            if (collider.gameObject.TryGetComponent<ContactBone>(out var bone)/* && collider.attachedRigidbody.TryGetComponent<PhysicsIgnoreHelpers>(out var temp) && temp.IsThisHandIgnored(this)*/)
            {
                return false;
            }
            if (collider.attachedRigidbody != null || collider != null)
            {
                return true;
            }
            return false;
        }

        private void OnDrawGizmos()
        {
            if (IsBoneContacting)
            {
                Gizmos.color = Color.red;
            }
            else if (IsBoneHovering)
            {
                Gizmos.color = Color.cyan;
            }
            else
            {
                Gizmos.color = Color.grey;
            }
            Gizmos.DrawSphere(_debugA, 0.001f);
            Gizmos.DrawSphere(_debugB, 0.001f);
            Gizmos.DrawLine(_debugA, _debugB);
        }

        #endregion
    }
}