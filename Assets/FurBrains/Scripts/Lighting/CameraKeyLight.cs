using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Keeps the scene's directional light shining from over the player's shoulder, so whatever the FurBrain faces
    /// (it turns toward the camera) is lit, and its shadow falls behind it.
    /// </summary>
    /// <remarks>
    /// Put this on the Directional Light. With a fixed light, walking round to the far side of the FurBrain meant
    /// looking at its unlit side, which only gets the dim ambient light, so it looked dark grey. Each frame this copies
    /// the camera's sideways turn (yaw) onto the light and keeps a fixed downward tilt, like a photographer's key
    /// light that moves with the camera.
    ///
    /// It runs in LateUpdate, after the AR camera has moved for this frame. It only sets the rotation: intensity,
    /// colour and shadows stay on the Light component.
    /// </remarks>
    [RequireComponent(typeof(Light))]
    public class CameraKeyLight : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Camera to follow. If empty, the main camera (the AR camera) is used.")]
        Camera m_Camera;

        [SerializeField, Range(10f, 90f)]
        [Tooltip("How steeply the light shines down, in degrees (90 = straight down).")]
        float m_DownAngle = 45f;

        [SerializeField, Range(-90f, 90f)]
        [Tooltip("Turns the light to one side of the camera, in degrees. A little to one side gives the face more shape.")]
        float m_SideAngle = 25f;

        /// <summary>Camera the light follows.</summary>
        public Camera followCamera
        {
            get => m_Camera;
            set => m_Camera = value;
        }

        void LateUpdate()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;

            // Only the camera's yaw (turn around the vertical axis): tilting the phone up or down shouldn't swing
            // the light. Euler(x, y, 0): x tilts the light's beam down, y turns it to match the camera.
            var yaw = m_Camera.transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(m_DownAngle, yaw + m_SideAngle, 0f);
        }
    }
}
