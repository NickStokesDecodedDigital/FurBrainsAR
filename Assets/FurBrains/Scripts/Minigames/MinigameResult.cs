namespace FurBrains
{
    /// <summary>How a minigame measures how it went.</summary>
    public enum MinigameResultKind
    {
        /// <summary>Points that go up (e.g. rally length). Rewards scale with the score.</summary>
        Score,

        /// <summary>Simply won or lost. Losing still gives a small reward (no punishing fail states).</summary>
        Outcome,
    }

    /// <summary>
    /// What a minigame reports when it ends. A small value type (a C# <c>struct</c> is copied like a C++ struct,
    /// unlike a <c>class</c>, which is always handled by reference).
    /// </summary>
    public readonly struct MinigameResult
    {
        /// <summary>Which of the fields below is meaningful.</summary>
        public readonly MinigameResultKind kind;

        /// <summary>Points scored (Score games).</summary>
        public readonly int score;

        /// <summary>Whether the player won (Outcome games).</summary>
        public readonly bool won;

        MinigameResult(MinigameResultKind kind, int score, bool won)
        {
            this.kind = kind;
            this.score = score;
            this.won = won;
        }

        /// <summary>A result for a game that counts points.</summary>
        public static MinigameResult FromScore(int score) => new(MinigameResultKind.Score, score, false);

        /// <summary>A result for a game that's won or lost.</summary>
        public static MinigameResult FromOutcome(bool won) => new(MinigameResultKind.Outcome, 0, won);

        /// <summary>Points for rewards: the score, or 1 for a win and 0 for a loss.</summary>
        public int points => kind == MinigameResultKind.Score ? score : won ? 1 : 0;

        /// <summary>Good enough for Ziara to celebrate: any points, or a win.</summary>
        public bool isGood => kind == MinigameResultKind.Score ? score > 0 : won;
    }
}
