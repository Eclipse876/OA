using UnityEngine;
using System;

namespace TGS {

    [Serializable]
    public struct TGSConfigEntry {
        public int cellIndex;
        public bool visible;
        public bool visibleAlways;
        public int territoryIndex;
        public Color color;
        public int textureIndex;
        public int tag;
        public bool canCross;
        public float crossCost; // all sides
        public float[] crossSidesCost; // per side
        public Vector2 textureScale, textureOffset;
        public TGSConfigAttribPair[] attribData;
    }


    [Serializable]
    public struct TGSConfigAttribPair {
        public string key;
        public string stringValue;
        public float numericValue;
    }

}