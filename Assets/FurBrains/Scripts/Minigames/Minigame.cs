using System;
using UnityEngine;

namespace FurBrains
{
    /// <summary>Everything a minigame gets handed when it starts.</summary>
    public class MinigameContext
    {
        /// <summary>The summoned FurBrain (play its reactions, e.g. <c>pet.OnTapped()</c>).</summary>
        public FurBrainPet pet;

        /// <summary>The FurBrain's transform (its position on the floor and facing).</summary>
        public Transform petTransform;

        /// <summary>The AR camera, i.e. the player's point of view.</summary>
        public Camera camera;
    }

    /// <summary>
    /// Base class for every minigame: the "pluggable module" interface. The runner starts any game the same way and
    /// gets a <see cref="MinigameResult"/> back, without knowing which game it is.
    /// </summary>
    /// <remarks>
    /// <b>Making a new minigame:</b>
    /// <list type="number">
    /// <item>Write a class deriving from Minigame. Set things up in <see cref="OnBegin"/>, run the game in Update, and
    /// end it with <see cref="FinishWithScore"/> or <see cref="FinishWithOutcome"/>.</item>
    /// <item>Make a prefab whose root has that component (plus any objects the game needs).</item>
    /// <item>Create a MinigameDefinition asset pointing at the prefab, and add it to the MinigameMenu's list.</item>
    /// </list>
    /// <c>abstract</c> works like a C++ class with a pure virtual function: you can't put a plain Minigame on an
    /// object, only a concrete game. <c>protected virtual</c> methods are the hooks a game overrides.
    /// The runner destroys the game object once the game has finished, so a game doesn't need to clean up after
    /// itself beyond what it created outside its own hierarchy.
    /// </remarks>
    public abstract class Minigame : MonoBehaviour
    {
        /// <summary>Raised once, when the game ends.</summary>
        public event Action<MinigameResult> finished;

        /// <summary>What the game was handed when it started.</summary>
        protected MinigameContext context { get; private set; }

        /// <summary>Whether the game has started and not yet finished.</summary>
        public bool isRunning { get; private set; }

        /// <summary>Which kind of result this game reports (decides what quitting early means).</summary>
        public abstract MinigameResultKind resultKind { get; }

        /// <summary>Points so far, for Score games (shown by the HUD, and kept if the player quits early).</summary>
        public int currentScore { get; protected set; }

        /// <summary>
        /// Seconds left for a timed game, or negative for a game without a timer. The HUD shows it as a shrinking bar.
        /// </summary>
        public float timeRemaining { get; protected set; } = -1f;

        /// <summary>Fraction of the time left (1 at the start, 0 when it runs out). Only meaningful when timed.</summary>
        public float timeRemainingNormalized { get; protected set; } = 1f;

        /// <summary>The FurBrain's height from its feet to the top of its head, in metres (measured at the start).</summary>
        protected float petHeight { get; private set; }

        /// <summary>Called by the runner to start the game.</summary>
        public void Begin(MinigameContext gameContext)
        {
            context = gameContext;
            isRunning = true;
            petHeight = MeasurePetHeight(context.petTransform);
            OnBegin();
        }

        /// <summary>Set up the game: create objects, position things around the FurBrain, start timers.</summary>
        protected abstract void OnBegin();

        /// <summary>Ends a game that counts points.</summary>
        protected void FinishWithScore(int score) => End(MinigameResult.FromScore(score));

        /// <summary>Ends a game that's won or lost.</summary>
        protected void FinishWithOutcome(bool won) => End(MinigameResult.FromOutcome(won));

        /// <summary>
        /// The player quit early (the X button). By default a Score game keeps the points so far and an Outcome game
        /// counts as not won, which still gives the small "lose" reward. Override to do something else.
        /// </summary>
        public virtual void Cancel()
        {
            if (resultKind == MinigameResultKind.Score)
                FinishWithScore(currentScore);
            else
                FinishWithOutcome(false);
        }

        /// <summary>Hook for cleanup or a last effect when the game ends (before the runner destroys it).</summary>
        protected virtual void OnFinished(MinigameResult result) { }

        // ---------- Helpers shared by all games (protected: only derived games can use them) ----------

        /// <summary>
        /// A point on the FurBrain at a fraction of its height (1 = top of its head), nudged slightly toward the
        /// player so things launched from it don't start inside the model.
        /// </summary>
        protected Vector3 PetPoint(float heightFraction)
        {
            var pet = context.petTransform;
            return pet.position + pet.up * (petHeight * heightFraction) + pet.forward * 0.05f;
        }

        /// <summary>
        /// True if the player tapped on <paramref name="target"/> this frame. Generous for small fingers: the tap
        /// counts within <paramref name="radiusMultiplier"/> × the object's on-screen radius of its centre, or within
        /// <paramref name="minScreenFraction"/> of the screen width, whichever is bigger. Taps on UI don't count.
        /// </summary>
        /// <param name="target">The object to test.</param>
        /// <param name="worldRadius">The object's radius in metres.</param>
        /// <param name="radiusMultiplier">How many on-screen radii away a tap still counts.</param>
        /// <param name="minScreenFraction">Smallest allowed tap distance, as a fraction of the screen width.</param>
        protected bool WasTapped(Transform target, float worldRadius, float radiusMultiplier = 1.5f,
            float minScreenFraction = 0.08f)
        {
            if (!TryGetTap(out var tap))
                return false;
            return IsTapNear(tap, target.position, worldRadius, radiusMultiplier, minScreenFraction);
        }

        /// <summary>
        /// The screen position of a tap that started this frame (ignoring taps on UI, e.g. the exit button).
        /// Use it with <see cref="IsTapNear"/> to test one tap against several objects.
        /// </summary>
        protected static bool TryGetTap(out Vector2 position)
        {
            position = default;
            // Pointer covers touch and mouse (Input System, the same input XRI reads).
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame)
                return false;
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events != null && events.IsPointerOverGameObject())
                return false;
            position = pointer.position.ReadValue();
            return true;
        }

        /// <summary>Whether a tap is close enough on screen to a world-space object (see <see cref="WasTapped"/>).</summary>
        protected bool IsTapNear(Vector2 tap, Vector3 worldPosition, float worldRadius, float radiusMultiplier,
            float minScreenFraction)
        {
            var cam = context.camera;
            var centre = cam.WorldToScreenPoint(worldPosition);
            if (centre.z <= 0f)
                return false; // behind the camera

            // On-screen radius: project a point one radius to the side and measure the distance.
            var edge = cam.WorldToScreenPoint(worldPosition + cam.transform.right * worldRadius);
            var radiusPixels = Vector2.Distance(centre, edge);
            var allowed = Mathf.Max(radiusPixels * radiusMultiplier, Screen.width * minScreenFraction);
            return Vector2.Distance(tap, centre) <= allowed;
        }

        /// <summary>Height from the FurBrain's feet to the top of its head (helmet included), from its renderers.</summary>
        static float MeasurePetHeight(Transform pet)
        {
            if (pet == null)
                return 0.6f; // fallback: the pet builder makes FurBrains 0.6 m tall
            var renderers = pet.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0.6f; // fallback: the pet builder makes FurBrains 0.6 m tall
            var top = float.MinValue;
            foreach (var r in renderers)
                top = Mathf.Max(top, r.bounds.max.y);
            return Mathf.Max(0.05f, top - pet.position.y);
        }

        /// <summary>Testing aid for the runner's context-menu items (internal: only code in this assembly can call it).</summary>
        internal void ForceEnd(MinigameResult result) => End(result);

        void End(MinigameResult result)
        {
            if (!isRunning)
                return; // already finished: ignore a second call
            isRunning = false;
            OnFinished(result);
            finished?.Invoke(result);
        }
    }
}
