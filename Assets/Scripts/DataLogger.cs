using System.IO;
using System.Text;
using UnityEngine;

namespace ResearchTCG
{
    /// <summary>
    /// Simple CSV logger for experimental use.
    /// Writes to Application.persistentDataPath/ResearchTCG/game_log.csv
    /// </summary>
    public class DataLogger
    {
        private readonly string logPath;
        private bool headerWritten;

        public DataLogger(string filename = "game_log.csv")
        {
            var dir = Path.Combine(Application.persistentDataPath, "ResearchTCG");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, filename);
            headerWritten = File.Exists(logPath);
            if (!headerWritten)
            {
                File.WriteAllText(logPath,
                    "timestamp_utc,participant,turn,player_card,player_attack,ai_card,ai_attack,result,player_score,ai_score,reaction_time_ms,hand_size,timed_out\n",
                    Encoding.UTF8);
                headerWritten = true;
            }
        }

        public void Append(string participant, int turn, Card pCard, Card aCard, string result,
                           int pScore, int aScore, float reactionTimeMs, int handSize, bool timedOut)
        {
            string safe(string s) => s == null ? "" : s.Replace(",", " ");
            var line = string.Format(
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10:F1},{11},{12}\n",
                System.DateTime.UtcNow.ToString("o"),
                safe(participant),
                turn,
                safe(pCard != null ? pCard.name : ""),
                pCard != null ? pCard.attack.ToString() : "",
                safe(aCard != null ? aCard.name : ""),
                aCard != null ? aCard.attack.ToString() : "",
                result,
                pScore,
                aScore,
                reactionTimeMs,
                handSize,
                timedOut ? 1 : 0
            );
            File.AppendAllText(logPath, line, Encoding.UTF8);
        }

        public string GetLogPath() => logPath;
    }
}