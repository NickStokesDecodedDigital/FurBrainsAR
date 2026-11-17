using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Measures a FurBrain's size from its visible body parts: the box around it and its height (feet to top of head).
    /// </summary>
    /// <remarks>
    /// Only mesh renderers count (Body, Mouth, Eyes, Helmet, Armor, Cape: SkinnedMeshRenderer or MeshRenderer). Effects
    /// such as the Eat Burst particle system are Renderers too, but their bounds depend on where their particles were
    /// last (and are meaningless before they first emit), so including them made the FurBrain seem taller than it is.
    /// A static class is like a C++ namespace of free functions: nothing to create or attach to a GameObject.
    /// </remarks>
    public static class FurBrainBounds
    {
        /// <summary>Height used when nothing can be measured: the pet builder makes FurBrains 0.6 m tall.</summary>
        public const float DefaultHeight = 0.6f;

        /// <summary>The world-space box around the FurBrain's body parts. False if it has none.</summary>
        public static bool TryGet(Transform pet, out Bounds bounds)
        {
            bounds = default;
            if (pet == null)
                return false;

            var found = false;
            foreach (var r in pet.GetComponentsInChildren<Renderer>())
            {
                // "is" checks the real type, like a C++ dynamic_cast that only asks "is it one of these?".
                var isBodyPart = r is SkinnedMeshRenderer || r is MeshRenderer;
                if (!isBodyPart || !r.enabled)
                    continue;

                if (!found)
                    bounds = r.bounds;        // the first part starts the box
                else
                    bounds.Encapsulate(r.bounds); // later parts grow it
                found = true;
            }
            return found;
        }

        /// <summary>Height from the FurBrain's feet (its pivot) to the top of its head, helmet included.</summary>
        public static float Height(Transform pet)
        {
            if (!TryGet(pet, out var bounds))
                return DefaultHeight;
            return Mathf.Max(0.05f, bounds.max.y - pet.position.y);
        }
    }
}
