using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace OpenWheelRacer
{
    /// <summary>
    /// Loads the project Input Actions asset and exposes the five Vehicle actions
    /// as clean float/bool reads for the controller.
    ///
    /// Deliberately does NOT depend on the C# script the Input Actions editor
    /// generates: that file only exists after someone selects the asset and presses
    /// "Generate C# Class", which headless runs can't rely on. We address actions
    /// by name instead, so the asset is the single source of truth and compilation
    /// never depends on editor-side codegen.
    /// </summary>
    public sealed class VehicleInput
    {
        private const string AssetPath = "Assets/Input/OpenWheelRacer.InputActions.inputactions";
        private const string VehicleMapName = "Vehicle";

        private InputActionAsset _asset;
        private InputActionMap _vehicleMap;

        public InputAction Throttle { get; private set; }
        public InputAction Brake { get; private set; }
        public InputAction Steer { get; private set; }
        public InputAction Handbrake { get; private set; }
        public InputAction Reset { get; private set; }

        /// <summary>Axis in -1..1 (positive = right).</summary>
        public float SteerAxis => Mathf.Clamp(Steer.ReadValue<float>(), -1f, 1f);

        /// <summary>0..1 analog trigger / key hold.</summary>
        public float ThrottleValue => Mathf.Clamp01(Throttle.ReadValue<float>());

        public float BrakeValue => Mathf.Clamp01(Brake.ReadValue<float>());

        public bool HandbrakePressed => Handbrake.ReadValue<float>() > 0.5f;

        public bool ResetPressed => Reset.ReadValue<float>() > 0.5f;

        /// <summary>
        /// Loads and enables the Vehicle map. Throws with a precise message if the
        /// asset or any expected action is missing — a loud boot failure beats a car
        /// that silently ignores input.
        /// </summary>
        public void Initialize(InputActionAsset explicitAsset = null)
        {
            _asset = explicitAsset;

#if UNITY_EDITOR
            _asset ??= UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
#endif

            _asset ??= Resources.Load<InputActionAsset>("Input/OpenWheelRacer.InputActions");

            if (_asset == null)
            {
                throw new System.IO.FileNotFoundException(
                    $"[VehicleInput] InputActionAsset not found at '{AssetPath}'.");
            }

            _vehicleMap = _asset.FindActionMap(VehicleMapName);
            if (_vehicleMap == null)
            {
                throw new System.InvalidOperationException(
                    $"[VehicleInput] Action map '{VehicleMapName}' missing from {_asset.name}.");
            }

            Throttle = RequireAction("Throttle");
            Brake = RequireAction("Brake");
            Steer = RequireAction("Steer");
            Handbrake = RequireAction("Handbrake");
            Reset = RequireAction("Reset");

            _vehicleMap.Enable();
            Debug.Log($"[VehicleInput] Enabled map '{VehicleMapName}' " +
                      $"({_vehicleMap.actions.Count} actions, asset '{_asset.name}').");
        }

        public void Shutdown()
        {
            _vehicleMap?.Disable();
        }

        private InputAction RequireAction(string actionName)
        {
            InputAction action = _vehicleMap.FindAction(actionName, throwIfNotFound: false);
            if (action == null)
            {
                throw new System.InvalidOperationException(
                    $"[VehicleInput] Action '{actionName}' not found in map '{VehicleMapName}'.");
            }
            return action;
        }
    }
}
