using System;
using UnityEngine;

namespace ResearchTCG
{
    [Serializable]
    public class Card
    {
        public string id;
        public string name;
        public int attack;
        public int cost;
        public string type;
        // Path relative to Resources folder without extension, e.g., "Sprites/fire"
        public string sprite;
    }

    [Serializable]
    public class CardList
    {
        public Card[] cards;
    }
}