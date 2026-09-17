    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.InputSystem;

    public class PlayerParty : MonoBehaviour
    {
        public static PlayerParty Instance { get; private set; }

        [Header("Spawning")]
        public Transform spawnPoint;           // fresh game; battles return to stored position
        public GameObject defaultAvatarPrefab; // fallback if a character has no overworldPrefab

        [Header("Follow Behaviour")]
        public float trailSampleDistance = 0.5f;
        public float trailSpacing = 1.15f;

        public int LeaderIndex { get { return leaderIndex; } }

        private readonly List<GameObject> avatars = new List<GameObject>();
        private readonly List<Vector3> trail = new List<Vector3>();
        private int leaderIndex;

        private void Awake() { Instance = this; }

        private void Start()
        {
            GameManager.EnsureExists();
            EncounterManager.ResetSteps();
            OverworldMenu.EnsureBuilt();

            SpawnParty();
        }

        private void SpawnParty()
        {
            GameManager gm = GameManager.Instance;
            Vector3 pos = gm.hasReturnPosition ? gm.returnPosition
                        : (spawnPoint != null ? spawnPoint.position : new Vector3(0f, 1.5f, 0f));
            gm.hasReturnPosition = false;
            SpawnPartyAt(pos);
        }

        private void SpawnPartyAt(Vector3 pos)
        {
            List<PartyMember> active = GameManager.Instance.ActiveParty();
            for (int i = 0; i < active.Count && i < 4; i++)
            {
                GameObject prefab = active[i].data.overworldPrefab != null
                    ? active[i].data.overworldPrefab : defaultAvatarPrefab;
                if (prefab == null)
                {
                    Debug.LogError("PlayerParty: no avatar prefab for " + active[i].data.characterName);
                    continue;
                }
                GameObject go = Instantiate(prefab, pos - new Vector3(trailSpacing * i, 0, 0),
                    Quaternion.identity, transform);
                avatars.Add(go);
            lastTrailY = pos.y;
            }

            trail.Clear();
            for (int i = avatars.Count; i >= 0; i--)
                trail.Add(pos - new Vector3(trailSpacing * i, 0, 0)); // [0] = farthest back

            leaderIndex = 0;
            AssignRoles();
        }

        private void Update()
        {
            RecordTrail();
            ReadLeaderSwap();
        }

        private float lastTrailY;   // last GROUNDED height — held while the leader is airborne

        private void RecordTrail()
        {
            if (avatars.Count == 0) return;
            Vector3 leaderPos = avatars[leaderIndex].transform.position;

            // ---- FOLLOWERS ARE GROUND-LOCKED ----
            // 1. Trail Y comes from the FLOOR under the leader (raycast down),
            //    never from the leader's airborne body — jumps never enter the trail.
            // 2. Sample distance is XZ-only — jumping straight up adds no points.
            // 3. While the leader is airborne over a GAP, HOLD the last grounded
            //    height instead of recording the gap floor — the trail crosses
            //    the gap, it doesn't dive into it.
            Vector3 groundPos = leaderPos;
            float groundY = GroundYUnder(leaderPos);
            if (leaderPos.y - groundY > 0.6f) groundY = lastTrailY;   // airborne — hold takeoff height
            else lastTrailY = groundY;
            groundPos.y = groundY;

            if (trail.Count == 0)
            {
                trail.Add(groundPos);
            }
            else
            {
                Vector3 last = trail[trail.Count - 1];
                Vector2 flat = new Vector2(groundPos.x - last.x, groundPos.z - last.z);
                if (flat.sqrMagnitude >= trailSampleDistance * trailSampleDistance)
                    trail.Add(groundPos);
            }

            int maxPoints = Mathf.CeilToInt((trailSpacing * 5f) / Mathf.Max(0.05f, trailSampleDistance)) + 2;
            if (trail.Count > maxPoints) trail.RemoveRange(0, trail.Count - maxPoints);
        }
        // Horizontal speed of the CURRENT leader (followers match this + margin).
        public float LeaderSpeed
        {
            get
            {
                if (avatars.Count == 0) return 0f;
                Rigidbody rb = avatars[leaderIndex].GetComponent<Rigidbody>();
                if (rb == null) return 0f;
                Vector3 v = rb.linearVelocity;
                return new Vector2(v.x, v.z).magnitude;
            }
        }
        // Floor height under a position — ignores the party's own colliders,
        // ignores triggers (encounter zones, NPC talk ranges).
        private float lastGroundY;
        private float GroundYUnder(Vector3 from)
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            int mask = playerLayer >= 0 ? ~(1 << playerLayer) : ~0;
            Vector3 origin = from + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 10f, mask, QueryTriggerInteraction.Ignore))
                lastGroundY = hit.point.y;
            return lastGroundY;
        }

        private void ReadLeaderSwap()
        {
            if (avatars.Count < 2 || OverworldMenu.IsOpen) return;
            if (GameManager.Instance != null && GameManager.Instance.inBattle) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            int next = -1;
            if (kb.tabKey.wasPressedThisFrame) next = (leaderIndex + 1) % avatars.Count;
            if (kb.digit1Key.wasPressedThisFrame) next = 0;
            if (kb.digit2Key.wasPressedThisFrame && avatars.Count > 1) next = 1;
            if (kb.digit3Key.wasPressedThisFrame && avatars.Count > 2) next = 2;
            if (kb.digit4Key.wasPressedThisFrame && avatars.Count > 3) next = 3;

            if (next >= 0 && next != leaderIndex) SetLeader(next);
        }

        public void SetLeader(int index)
        {
            if (index < 0 || index >= avatars.Count) return;
            leaderIndex = index;
            AssignRoles();
        }

        private void AssignRoles()
        {
            int followerCount = 0;
            for (int i = 0; i < avatars.Count; i++)
            {
                bool isLeader = i == leaderIndex;
                GameObject go = avatars[i];

                PlayerController pc = go.GetComponent<PlayerController>();
                if (pc != null) pc.enabled = isLeader;

                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = !isLeader;

                CapsuleCollider col = go.GetComponent<CapsuleCollider>();
                if (col != null) col.isTrigger = !isLeader; // followers never shove the leader

                PartyFollower pf = go.GetComponent<PartyFollower>();
                if (pf != null)
                {
                    pf.enabled = !isLeader;
                    if (!isLeader) { pf.followIndex = followerCount; pf.Party = this; followerCount++; }
                }
            }

            CameraFollow cam = FindFirstObjectByType<CameraFollow>();
            if (cam != null && avatars.Count > 0) cam.target = avatars[leaderIndex].transform;
        }

        public Vector3 GetLeaderPosition()
        {
            return avatars.Count > 0 ? avatars[leaderIndex].transform.position : transform.position;
        }

        public Vector3 GetTrailPoint(int followerIndex)
        {
            float wanted = trailSpacing * (followerIndex + 1);
            if (trail.Count == 0) return transform.position;

            Vector3 prev = trail[trail.Count - 1];
            float accumulated = 0f;
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                Vector3 cur = trail[i];
                float seg = new Vector2(cur.x - prev.x, cur.z - prev.z).magnitude; // XZ only
                accumulated += seg;
                if (accumulated >= wanted)
                {
                    float overshoot = accumulated - wanted;
                    if (seg <= 0.0001f) return cur;
                    return Vector3.Lerp(prev, cur, 1f - (overshoot / seg));
                }
                prev = cur;
            }
            return trail[0];
        }

        // Called by the menu after reordering / changing active members.
        public void RebuildFromParty()
        {
            Vector3 leaderPos = avatars.Count > 0 ? GetLeaderPosition()
                : (spawnPoint != null ? spawnPoint.position : new Vector3(0f, 1.5f, 0f));

            foreach (GameObject go in avatars) Destroy(go);
            avatars.Clear();
            trail.Clear();
            leaderIndex = 0;
            SpawnPartyAt(leaderPos);
        }
    }