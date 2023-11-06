using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Leap.Unity.PhysicalHands
{
    public abstract class ContactParent : MonoBehaviour
    {
        private ContactHand _leftHand;
        public ContactHand LeftHand => _leftHand;

        private ContactHand _rightHand;
        public ContactHand RightHand => _rightHand;

        public PhysicalHandsManager _physicalHandsManager;

        public PhysicalHandsManager PhysicalHandsManager => _physicalHandsManager;

        private void Start()
        {
            _physicalHandsManager = GetComponentInParent<PhysicalHandsManager>();
            GenerateHands();

            PhysicalHandsManager.HandsInitiated();
        }

        internal abstract void GenerateHands();

        internal void GenerateHandsObjects(System.Type handType, bool callGenerate = true)
        {
            GameObject handObject = new GameObject($"Left {handType.Name}", handType);
            handObject.transform.parent = transform;
            _leftHand = handObject.GetComponent<ContactHand>();
            _leftHand._handedness = Chirality.Left;
            if (callGenerate)
            {
                _leftHand.GenerateHand();
            }

            handObject = new GameObject($"Right {handType.Name}", handType);
            handObject.transform.parent = transform;
            _rightHand = handObject.GetComponent<ContactHand>();
            _rightHand._handedness = Chirality.Right;
            if (callGenerate)
            {
                _rightHand.GenerateHand();
            }
        }

        internal void UpdateFrame()
        {
            UpdateHand(PhysicalHandsManager._leftHandIndex, _leftHand, PhysicalHandsManager._leftDataHand);
            UpdateHand(PhysicalHandsManager._rightHandIndex, _rightHand, PhysicalHandsManager._rightDataHand);
        }

        private void UpdateHand(int index, ContactHand hand, Hand dataHand)
        {
            if (hand.IsHandPhysical && !Time.inFixedTimeStep)
            {
                return;
            }
            if (index != -1)
            {
                hand.dataHand.CopyFrom(dataHand);
                if (!hand.tracked)
                {
                    hand.BeginHand(dataHand);
                }
                // Actually call the update once the hand is ready
                if (hand.tracked)
                {
                    hand.UpdateHand(dataHand);
                }
            }
            else
            {
                if (hand.tracked || hand.resetting)
                {
                    hand.FinishHand();
                }
            }
        }

        internal void OutputFrame(ref Frame inputFrame)
        {
            PhysicalHandsManager._leftHandIndex = inputFrame.Hands.FindIndex(x => x.IsLeft);
            OutputHand(PhysicalHandsManager._leftHandIndex, _leftHand, ref inputFrame);
            PhysicalHandsManager._rightHandIndex = inputFrame.Hands.FindIndex(x => x.IsRight);
            OutputHand(PhysicalHandsManager._rightHandIndex, _rightHand, ref inputFrame);
        }

        private void OutputHand(int index, ContactHand hand, ref Frame inputFrame)
        {
            if (index == -1)
            {
                if (hand.tracked)
                {
                    inputFrame.Hands.Add(hand.OutputHand());
                }
            }
            else
            {
                if (hand.tracked)
                {
                    inputFrame.Hands[index].CopyFrom(hand.OutputHand());
                }
                else if(inputFrame.Hands.Count > 0)
                {
                    inputFrame.Hands.RemoveAt(index);
                }
            }
        }

        internal void PostFixedUpdateFrame()
        {
            PostFixedUpdateFrameLogic();
            _leftHand.PostFixedUpdateHand();
            _rightHand.PostFixedUpdateHand();
        }

        /// <summary>
        /// Happens before the hands update is called.
        /// </summary>
        internal abstract void PostFixedUpdateFrameLogic();

        internal void ProcessHandIntersection()
        {

        }

        internal void ProcessHandOverlaps()
        {

        }

        protected virtual void OnValidate()
        {
            if (PhysicalHandsManager == null)
            {
                _physicalHandsManager = GetComponentInParent<PhysicalHandsManager>();
            }
        }
    }
}