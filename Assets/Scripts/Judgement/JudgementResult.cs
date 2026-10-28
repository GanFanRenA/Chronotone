/// <summary>一次判定结算的完整结果。</summary>
public readonly struct JudgementResult
{
    public JudgementResult(
        int noteIndex,
        JudgementGrade grade,
        double timeDelta,
        double songTime,
        bool noteConsumed)
    {
        NoteIndex = noteIndex;
        Grade = grade;
        TimeDelta = timeDelta;
        SongTime = songTime;
        NoteConsumed = noteConsumed;
    }

    public int NoteIndex { get; }
    public JudgementGrade Grade { get; }

    /// <summary>正值表示晚于 Note，负值表示早于 Note。</summary>
    public double TimeDelta { get; }

    public double SongTime { get; }
    public bool NoteConsumed { get; }
    public bool HasNote => NoteIndex >= 0;
    public bool IsSuccessfulHit =>
        NoteConsumed &&
        (Grade == JudgementGrade.Perfect ||
         Grade == JudgementGrade.Great ||
         Grade == JudgementGrade.Bad);
}

public enum JudgementGrade
{
    Perfect,
    Great,
    Bad,
    Miss
}
