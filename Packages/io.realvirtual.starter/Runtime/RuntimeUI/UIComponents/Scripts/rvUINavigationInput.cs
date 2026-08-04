// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// Copyright(c) 2019 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz

using UnityEngine;
using UnityEngine.EventSystems;

namespace realvirtual
{
    //! Hardens EventSystem UI navigation against stuck / continuously-applied device axes.
    //!
    //! The uGUI input module derives UI navigation from BaseInput.GetAxisRaw("Vertical"/"Horizontal").
    //! In a default Unity project those Input Manager axes also carry a JOYSTICK binding, so a continuously
    //! non-zero device signal (a 3D-mouse / SpaceMouse driver's virtual device, a drifting gamepad) makes the
    //! module fire navigation moves every frame and the selection "runs through" - even with the device
    //! physically unplugged, because the driver keeps a virtual device alive.
    //!
    //! This BaseInput derives the navigation axes from KEYBOARD keys only and ignores device/joystick axes.
    //! It registers itself as the module's inputOverride in OnEnable, because uGUI does NOT auto-use a derived
    //! BaseInput (BaseInputModule only auto-picks a component whose type is exactly BaseInput). Full keyboard
    //! navigation (arrow keys / WASD, Enter, Esc) keeps working; a stuck device axis is ignored, so
    //! "Send Navigation Events" can stay enabled without the run-through. Add to the EventSystem GameObject.
    //!
    //! Note: only the UI navigation axes are remapped. SpaceMouse 3D camera navigation runs through a different
    //! path (SceneMouseNavigation / 3Dconnexion SDK) and is unaffected.
    [RequireComponent(typeof(EventSystem))]
    [AddComponentMenu("realvirtual/UI/UI Navigation Input (keyboard, device-axis safe)")]
    public class rvUINavigationInput : BaseInput
    {
        //! Registers this keyboard-only input as the inputOverride on every input module of this EventSystem.
        protected override void OnEnable()
        {
            base.OnEnable();
            var modules = GetComponents<BaseInputModule>();
            foreach (var module in modules)
                module.inputOverride = this;
        }

        //! Removes this input override again so the module falls back to its default input.
        protected override void OnDisable()
        {
            var modules = GetComponents<BaseInputModule>();
            foreach (var module in modules)
                if (module.inputOverride == this)
                    module.inputOverride = null;
            base.OnDisable();
        }

        //! Returns the keyboard-only value for the navigation axes, otherwise the default raw axis.
        public override float GetAxisRaw(string axisName)
        {
            if (axisName == "Vertical")
                return KeyboardVertical();
            if (axisName == "Horizontal")
                return KeyboardHorizontal();
            return base.GetAxisRaw(axisName);
        }

        private static float KeyboardVertical()
        {
            float v = 0f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) v += 1f;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v -= 1f;
            return v;
        }

        private static float KeyboardHorizontal()
        {
            float h = 0f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) h += 1f;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) h -= 1f;
            return h;
        }
    }
}
