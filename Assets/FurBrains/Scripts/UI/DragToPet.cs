using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FurBrains
{
    /// <summary>
    /// Lets the player drag a menu button's picture onto the FurBrain. A copy of the icon follows the finger; letting
    /// go over (or near) the FurBrain runs the button's action (for food: buy it and eat it), letting go anywhere
    /// else does nothing. Tapping the button still works as before.
    /// </summary>
    /// <remarks>
    /// <b>How Unity UI drags work:</b> the EventSystem sends drag "messages" to the UI object under the finger,
    /// calling methods from interfaces it implements: IBeginDragHandler.OnBeginDrag when the finger has moved a few
    /// pixels, IDragHandler.OnDrag every move, IEndDragHandler.OnEndDrag on release. Implementing an interface is like
    /// inheriting a C++ pure-virtual interface: the EventSystem calls through it without knowing this class.
    ///
    /// <b>Sharing the drag with the scroll list:</b> the food buttons sit in a ScrollRect that scrolls sideways, and a
    /// drag on a button would normally scroll it. The EventSystem sends a drag to only one object, now this one, so a
    /// mostly sideways drag is passed on to the ScrollRect by calling its own handlers, and only a drag that starts
    /// upward (toward the FurBrain) picks the food up.
    ///
    /// <b>Keeping the menu open:</b> ARTemplateMenuManager closes the menu when the finger is down outside it, which
    /// would happen halfway through every drag. It checks <see cref="isDragging"/> and leaves the menu open meanwhile.
    ///
    /// Added to each food button by FoodMenu (the buttons are created at runtime), which passes in what to do on a
    /// drop and when dragging is allowed, the same two functions the button's tap uses.
    /// </remarks>
    public class DragToPet : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler
    {
        // Static: shared by all buttons (only one finger drags at a time). Read by ARTemplateMenuManager.
        /// <summary>True while any menu item is being dragged toward the FurBrain.</summary>
        public static bool isDragging { get; private set; }

        const float k_DragScale = 1.3f;      // the dragged picture is a bit bigger than the button's icon
        const float k_OverPetScale = 1.6f;   // and grows when it's over the FurBrain ("let go here!")
        const float k_DropMargin = 0.08f;    // how far outside the FurBrain still counts, as a fraction of screen width

        Sprite m_Icon;
        Action m_OnDrop;
        Func<bool> m_CanDrag;
        FurBrainSummoner m_Summoner;

        ScrollRect m_ScrollRect;    // the menu's scroll list, given sideways drags
        bool m_PassingToScroll;     // this drag belongs to the scroll list
        RectTransform m_Ghost;      // the picture following the finger (null when not dragging)
        float m_GhostSize;

        /// <summary>
        /// Sets up the button: the picture to drag, what to do when it's dropped on the FurBrain, and when dragging is
        /// allowed (e.g. food you can afford, for a FurBrain that isn't full).
        /// </summary>
        public void Setup(Sprite icon, Action onDrop, Func<bool> canDrag, FurBrainSummoner summoner)
        {
            m_Icon = icon;
            m_OnDrop = onDrop;
            m_CanDrag = canDrag;
            m_Summoner = summoner;
        }

        void Awake() => m_ScrollRect = GetComponentInParent<ScrollRect>();

        // Called on press, before any drag: the ScrollRect uses it to stop its momentum.
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (m_ScrollRect != null)
                m_ScrollRect.OnInitializePotentialDrag(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Mostly upward = pick up the food; anything else = scroll the list as before.
            var delta = eventData.position - eventData.pressPosition;
            var upward = delta.y > Mathf.Abs(delta.x);
            if (!upward || m_Icon == null || (m_CanDrag != null && !m_CanDrag()))
            {
                m_PassingToScroll = true;
                if (m_ScrollRect != null)
                    m_ScrollRect.OnBeginDrag(eventData);
                return;
            }

            m_PassingToScroll = false;
            isDragging = true;
            CreateGhost();
            MoveGhost(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (m_PassingToScroll)
            {
                if (m_ScrollRect != null)
                    m_ScrollRect.OnDrag(eventData);
                return;
            }
            MoveGhost(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (m_PassingToScroll)
            {
                m_PassingToScroll = false;
                if (m_ScrollRect != null)
                    m_ScrollRect.OnEndDrag(eventData);
                return;
            }

            var dropped = IsOverPet(eventData.position);
            EndDrag();
            // Check again on release: the coins or hunger might have changed during the drag.
            if (dropped && (m_CanDrag == null || m_CanDrag()))
                m_OnDrop?.Invoke();
        }

        // If the button is switched off mid-drag (e.g. a minigame starts), don't leave the picture or flag behind.
        void OnDisable()
        {
            if (m_Ghost != null)
                EndDrag();
            m_PassingToScroll = false;
        }

        void CreateGhost()
        {
            // A plain Image on the top-level canvas, so it draws above the menu and isn't clipped by the scroll list.
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            var go = new GameObject("Dragged Item", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsLastSibling(); // drawn last = on top of everything
            var image = go.AddComponent<Image>();
            image.sprite = m_Icon;
            image.preserveAspect = true;
            image.raycastTarget = false; // must not block the finger, or "is the finger over UI" checks would see it

            // Size it like the button's own icon (which is scaled with the menu), times k_DragScale.
            var iconRect = transform.Find("ObjectIcon") as RectTransform;
            var reference = iconRect != null ? iconRect : (RectTransform)transform;
            var worldSize = reference.rect.width * reference.lossyScale.x;
            m_GhostSize = worldSize / canvas.transform.lossyScale.x;
            m_Ghost = (RectTransform)go.transform;
            m_Ghost.sizeDelta = Vector2.one * m_GhostSize * k_DragScale;
        }

        void MoveGhost(PointerEventData eventData)
        {
            if (m_Ghost == null)
                return;
            // Screen position → a point on the canvas (works for every Canvas render mode).
            var canvasRect = (RectTransform)m_Ghost.parent;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, eventData.position,
                    eventData.pressEventCamera, out var world))
                m_Ghost.position = world;

            // Grow a little when it's over the FurBrain, so the child sees "let go here".
            var scale = IsOverPet(eventData.position) ? k_OverPetScale : k_DragScale;
            m_Ghost.sizeDelta = Vector2.one * m_GhostSize * scale;
        }

        void EndDrag()
        {
            isDragging = false;
            if (m_Ghost != null)
                Destroy(m_Ghost.gameObject); // deferred to the end of the frame, like every Destroy
            m_Ghost = null;
        }

        /// <summary>
        /// Whether a screen position is on or near the FurBrain: inside the screen rectangle that its 3D bounds cover,
        /// widened by a margin so small fingers don't need to be exact.
        /// </summary>
        bool IsOverPet(Vector2 screenPosition)
        {
            var cam = Camera.main;
            if (m_Summoner == null || !m_Summoner.hasSummoned || cam == null)
                return false;

            // The FurBrain's 3D box: all its renderers' bounds merged (Encapsulate grows a box to include another).
            var renderers = m_Summoner.summoned.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return false;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            // Project the box's 8 corners onto the screen and take the rectangle around them.
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var screen = cam.WorldToScreenPoint(corner);
                if (screen.z <= 0f)
                    return false; // the FurBrain is (partly) behind the camera
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            var margin = Screen.width * k_DropMargin;
            return screenPosition.x >= min.x - margin && screenPosition.x <= max.x + margin &&
                   screenPosition.y >= min.y - margin && screenPosition.y <= max.y + margin;
        }
    }
}
