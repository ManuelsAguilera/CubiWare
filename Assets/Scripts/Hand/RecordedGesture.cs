using System;
using System.Collections.Generic;
using UnityEngine;
using Mediapipe.Tasks.Components.Containers;

namespace ARcadeRush.Hand
{
    [Serializable]
    public class HandSnapshot
    {
        public List<Vector2> NormalizedLandmarks = new List<Vector2>();

        public HandSnapshot(NormalizedLandmarks landmarks)
        {
            if (landmarks.landmarks == null) return;
            foreach (var l in landmarks.landmarks)
            {
                NormalizedLandmarks.Add(new Vector2(l.x, l.y));
            }
        }
    }

    [Serializable]
    public class RecordedGesture
    {
        public string GestureName;
        public List<HandSnapshot> Snapshots = new List<HandSnapshot>();
        public DateTime RecordedAt;

        public RecordedGesture(string name)
        {
            GestureName = name;
            RecordedAt = DateTime.Now;
        }
    }

    [Serializable]
    public class GestureDatabase
    {
        public List<RecordedGesture> Gestures = new List<RecordedGesture>();
    }
}
