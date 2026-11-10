using UnityEngine;

namespace FurBrains
{
    /// <summary>
    /// Gives a FurBrain shell fur: adds one "FurBrains/ShellFur" material per shell to the body's renderer.
    /// </summary>
    /// <remarks>
    /// The trick: when a renderer has more materials than its mesh has sub-meshes, Unity draws the last sub-mesh again
    /// with each extra material. (That's what caused the z-fighting eye earlier; here it's exactly what we want.)
    /// The Body mesh has a single sub-mesh, so every extra material is one more full copy of the body, i.e. one shell.
    /// Because the SkinnedMeshRenderer draws them itself, the shells follow the skeleton, animations and blend shapes
    /// for free, with the skinning only computed once.
    ///
    /// The shell materials are created at runtime (<c>new Material(...)</c>, Unity's equivalent of <c>new</c>) and
    /// must be destroyed explicitly (<c>Destroy</c>, like <c>delete</c>): the C# garbage collector only frees the small
    /// C# wrapper, not the engine-side material. This only runs in Play mode, so the prefab asset is never modified.
    /// </remarks>
    public class FurBrainFur : MonoBehaviour
    {
        static readonly int k_ShellHeight = Shader.PropertyToID("_ShellHeight");

        [SerializeField]
        [Tooltip("Renderer that grows fur. If empty, the child SkinnedMeshRenderer named \"Body\" is used.")]
        SkinnedMeshRenderer m_Renderer;

        [SerializeField]
        [Tooltip("Template material using the FurBrains/ShellFur shader. Edit this to tune the fur (live in Play mode).")]
        Material m_FurMaterial;

        [SerializeField, Range(1, 32)]
        [Tooltip("Number of shell layers. More looks smoother but redraws the body once per shell.")]
        int m_ShellCount = 16;

        Material[] m_OriginalMaterials;
        Material[] m_Shells;

        /// <summary>Template material using the FurBrains/ShellFur shader.</summary>
        public Material furMaterial
        {
            get => m_FurMaterial;
            set => m_FurMaterial = value;
        }

        /// <summary>Number of shell layers.</summary>
        public int shellCount
        {
            get => m_ShellCount;
            set { m_ShellCount = Mathf.Clamp(value, 1, 32); Rebuild(); }
        }

        void OnEnable() => AddShells();

        void OnDisable() => RemoveShells();

        void OnValidate()
        {
            // Changing the shell count in the Inspector during Play mode rebuilds the shells.
            if (isActiveAndEnabled && InRunningScene())
                Rebuild();
        }

        /// <summary>
        /// True only for objects in the running game. Excludes Prefab Mode, which also exists during Play mode but
        /// edits the prefab asset itself: adding shells there would save runtime-only materials into the prefab file
        /// (they come back as missing, magenta materials).
        /// </summary>
        bool InRunningScene()
        {
            if (!Application.isPlaying)
                return false;
#if UNITY_EDITOR
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene))
                return false;
#endif
            return true;
        }

#if UNITY_EDITOR
        void Update()
        {
            // Editor convenience: copy the template's settings to the shells every frame, so tweaking the fur
            // material in Play mode shows up immediately. Skipped in builds.
            if (m_Shells == null || m_FurMaterial == null)
                return;
            for (var i = 0; i < m_Shells.Length; i++)
            {
                m_Shells[i].CopyPropertiesFromMaterial(m_FurMaterial);
                m_Shells[i].SetFloat(k_ShellHeight, ShellHeight(i));
            }
        }
#endif

        void Rebuild()
        {
            RemoveShells();
            AddShells();
        }

        void AddShells()
        {
            if (m_Shells != null || m_FurMaterial == null || !InRunningScene())
                return;
            if (m_Renderer == null)
                m_Renderer = FindBody();
            if (m_Renderer == null || m_Renderer.sharedMesh == null)
            {
                Debug.LogWarning($"[FurBrains] {name}: no body renderer found for fur.", this);
                return;
            }
            if (m_Renderer.sharedMesh.subMeshCount != 1)
                Debug.LogWarning($"[FurBrains] {name}: {m_Renderer.name} has {m_Renderer.sharedMesh.subMeshCount} sub-meshes; " +
                                 "fur only grows on the last one.", this);

            m_OriginalMaterials = m_Renderer.sharedMaterials;
            var subMeshCount = m_Renderer.sharedMesh.subMeshCount;

            m_Shells = new Material[m_ShellCount];
            var materials = new Material[subMeshCount + m_ShellCount];
            for (var i = 0; i < subMeshCount; i++)
                materials[i] = i < m_OriginalMaterials.Length ? m_OriginalMaterials[i] : null;

            for (var i = 0; i < m_ShellCount; i++)
            {
                var shell = new Material(m_FurMaterial) { name = $"{m_FurMaterial.name} (Shell {i + 1})" };
                shell.SetFloat(k_ShellHeight, ShellHeight(i));
                // Inner shells first, so outer hairs draw over inner ones.
                shell.renderQueue = m_FurMaterial.renderQueue + i;
                m_Shells[i] = shell;
                materials[subMeshCount + i] = shell;
            }
            m_Renderer.sharedMaterials = materials;
        }

        void RemoveShells()
        {
            if (m_Shells == null)
                return;
            if (m_Renderer != null)
                m_Renderer.sharedMaterials = m_OriginalMaterials;
            foreach (var shell in m_Shells)
                Destroy(shell);
            m_Shells = null;
            m_OriginalMaterials = null;
        }

        float ShellHeight(int index) => (index + 1f) / m_ShellCount;

        SkinnedMeshRenderer FindBody()
        {
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == "Body")
                    return r;
            return null;
        }
    }
}
