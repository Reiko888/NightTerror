using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;


namespace NightTerror
{
    public class NightTerrorAI : EnemyAI
    {
        //when in hunt mode, swap to monster container. Spawn clone prefab of doll at doll location before swapping 

        public GameObject dollPrefab;
        public GameObject monsterContainer;
        public GameObject dollContainer;
        private GameObject FakeDoll;
        public Animator dollAnimator;
        public Animator monsterAnimator;
        public GameObject EnemyCollider;

        public AudioSource monsterDistant;
        public AudioClip[] askHideSeekVoiceLines;
        public AudioClip[] foundVoiceLines;
        public AudioClip[] countVoiceLines;
        public AudioClip[] chaseWindChimes;
        public AudioClip[] distantRoars;
        public AudioClip[] roamingYapping;
        public AudioClip[] huntingTaunts;
        public AudioClip[] doorSlam;
        public AudioClip[] readytoFind;

        private DoorLock[] cachedDoors;

        public Material monsterMat;

        private bool isInDollForm = true;
        private bool targetHasFled = false;
        private bool isCounting = false;
        private float foundPlayerTimer = 5f;
        private float countDuration;        
        private bool isSeeking = false;
        private bool isFoundPhaseStarted = false;
        private bool hasSummonedMonster = false;
        private float manifestTimer;
        private bool hasRoared;
        private float randomRoamTimer;
        private float yapCooldown;
        private Quaternion currentRot;
        private float chompCooldown;
        private float breakDoorTimer;

        private float roamingSearchWidth;
        private float seekingSearchWidth;
        private float roamingSpeed;
        private float seekingSpeed;
        private float chaseSpeed;

        private System.Random enemyRandom;

        public AISearchRoutine wanderFacility;
        public AISearchRoutine seeking;
        private Coroutine FoundPhase;

        private Vector3 currentPos;

        //Behaviour States
        //0- Searching
        //1- CountingDown
        //2- Seeeking
        //3- SummonChase

        public override void Start()
        {
            base.Start();
            roamingSpeed = ModContentHandler.Instance.nightTerrorEnemyAssets.GetConfig<float>("Roaming speed").Value;
            seekingSpeed = ModContentHandler.Instance.nightTerrorEnemyAssets.GetConfig<float>("Seeking speed").Value;
            chaseSpeed = ModContentHandler.Instance.nightTerrorEnemyAssets.GetConfig<float>("Hunting speed").Value;
            countDuration= ModContentHandler.Instance.nightTerrorEnemyAssets.GetConfig<float>("Counting duration").Value;
            roamingSearchWidth = ModContentHandler.Instance.nightTerrorEnemyAssets.GetConfig<float>("Roaming search width").Value;
            seekingSearchWidth = ModContentHandler.Instance.nightTerrorEnemyAssets.GetConfig<float>("Seeking search width").Value;
            if (wanderFacility != null) //customise search routine
            {
                wanderFacility.searchWidth = roamingSearchWidth;
            }
            if (seeking != null) //customise search routine
            {
                seeking.searchWidth = seekingSearchWidth;
            }
            cachedDoors = UnityEngine.Object.FindObjectsOfType<DoorLock>();
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            monsterMat.SetFloat("_AlphaCutoff", 1f);

        }



        public override void Update()
        {
            base.Update();
            
            if (base.IsOwner)
            {
                if (breakDoorTimer > 0f)
                {
                    breakDoorTimer -= Time.deltaTime;
                }
                chompCooldown += Time.deltaTime;
                if (targetPlayer != null && !targetPlayer.isInsideFactory && currentBehaviourStateIndex == 3 && !targetHasFled && IsOwner) 
                {
                    manifestTimer += Time.deltaTime;
                    if (manifestTimer > 4f)
                    {
                        Debug.Log("[NightTerrorAI] Target has fled!");
                        targetHasFled = true;
                    }
                }
            }

            

            if (currentBehaviourStateIndex != 1 && currentBehaviourStateIndex != 3)
            {
                yapCooldown += Time.deltaTime;
                randomRoamTimer += Time.deltaTime;
                if (randomRoamTimer > 5 && yapCooldown>10f)
                {
                    randomRoamTimer = 0;
                    float random = UnityEngine.Random.Range(1, 10);
                    if (random > 5)
                    {
                        UnityEngine.Debug.Log($"[NightTerror] Playing random roam/hunt SFX");
                        if (currentBehaviourStateIndex == 0)
                        {
                            creatureSFX.Stop();
                            creatureSFX.PlayOneShot(roamingYapping[enemyRandom.Next(0, roamingYapping.Length)]);
                            yapCooldown = 0f;
                        }
                        else if (currentBehaviourStateIndex == 2)
                        {
                            creatureSFX.Stop();
                            creatureSFX.PlayOneShot(huntingTaunts[enemyRandom.Next(0, huntingTaunts.Length)]);
                            yapCooldown = 0f;
                        }
                    }
                }
            }


            bool shouldEnableMonster = (currentBehaviourStateIndex == 3);
            dollContainer.SetActive(!shouldEnableMonster);
            //monsterContainer.SetActive(shouldEnableMonster);

            if (GameNetworkManager.Instance.localPlayerController != null && !GameNetworkManager.Instance.localPlayerController.isPlayerDead)
            {
                if (currentBehaviourStateIndex == 3) //Only in chase
                {
                    if (GameNetworkManager.Instance.localPlayerController.HasLineOfSightToPosition(transform.position, 50f, 25, 10f))
                    {
                        GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(1f); //Max fear
                    }
                    else
                    {
                        if (GameNetworkManager.Instance.localPlayerController.HasLineOfSightToPosition(transform.position, 50f, 30, 5f))
                        {
                            GameNetworkManager.Instance.localPlayerController.IncreaseFearLevelOverTime(0.25f, 0.3f); //moderate fear
                        }
                    }
                }
            }
        }


        [ServerRpc(RequireOwnership = false)]
        private void SetMonsterVisibleServerRpc(float phaseLength)
        {
            SetMonsterVisibleClientRpc(phaseLength);
        }

        [ClientRpc]
        private void SetMonsterVisibleClientRpc( float phaseLength)
        {
            StartCoroutine(SetMonsterVisibility(phaseLength));
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetMonsterInvisibleServerRpc(float phaseLength)
        {
            SetMonsterInvisibleClientRpc(phaseLength);
        }

        [ClientRpc]
        private void SetMonsterInvisibleClientRpc(float phaseLength)
        {
            StartCoroutine(SetMonsterInvisibility(phaseLength));
        }


        private IEnumerator SetMonsterVisibility(float phaseLength)
        {
            monsterContainer.SetActive(true);
            float elapsed = 0f;
            while (elapsed < phaseLength)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / phaseLength);
                float cutoff = Mathf.Lerp(1f, 0.01f, progress);
                monsterMat.SetFloat("_AlphaCutoff", cutoff);
                yield return null;

            }
            monsterMat.SetFloat("_AlphaCutoff", 0.01f);

            PlayerControllerB localPlayerController = GameNetworkManager.Instance.localPlayerController;
            
            
        }

        private IEnumerator SetMonsterInvisibility(float phaseLength)
        {
            if (GameNetworkManager.Instance.localPlayerController)
            {
                HUDManager.Instance.UIAudio.PlayOneShot(chaseWindChimes[enemyRandom.Next(0, chaseWindChimes.Length)], 0.4f);
            }
            float elapsed = 0f;
            while (elapsed < phaseLength)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / phaseLength);
                float cutoff = Mathf.Lerp(0.01f, 1f, progress);
                monsterMat.SetFloat("_AlphaCutoff", cutoff);
                yield return null;
            }
            monsterMat.SetFloat("_AlphaCutoff", 1f);
            monsterContainer.SetActive(false);
            AfterChaseCleanUpServerRpc();
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            switch (currentBehaviourStateIndex)
            {
                case 0:
                    agent.speed = roamingSpeed;
                    if (base.IsOwner)
                    {
                        hasRoared = false;
                        if (!wanderFacility.inProgress)
                        {
                            StartSearch(base.transform.position, wanderFacility);
                        }
                        if (TargetClosestPlayer(bufferDistance: 4f, requireLineOfSight: true))
                        {
                            Debug.Log("[NightTerrorAI] Found player.");
                            StopSearch(wanderFacility);
                            PlayerControllerB playerControllerB = targetPlayer;
                            SwitchToBehaviourState(1);
                        }
                    }
                    break;
                case 1:
                    if (base.IsOwner)
                    {
                        if (!isCounting)
                        {
                            Debug.Log("[NightTerrorAI] Counting");
                            isCounting = true;
                            StartCountingServerRpc();
                        }
                    }

                    break;
                case 2:
                    if (base.IsOwner)
                    {
                        if (isFoundPhaseStarted)
                        {
                            break;
                        }

                        if (TargetClosestPlayer(bufferDistance: 4f, requireLineOfSight: true))
                        {
                            Debug.Log("[NightTerrorAI] Player found after seek!");
                            isFoundPhaseStarted = true;
                            if (seeking != null && seeking.inProgress)
                            {
                                StopSearch(seeking);
                            }
                            foundPlayerTimer = 5f;
                            isSeeking = false;
                            FoundPlayerServerRpc();
                            break;
                        }

                        if (seeking != null && !seeking.inProgress)
                        {
                            isSeeking = true;
                            Debug.Log("[NightTerrorAI] Starting to seek!");
                            StartSearch(base.transform.position, seeking);
                        }
                    }
                    agent.speed = seekingSpeed;
                    break;
                case 3:
                    if (!hasRoared)
                    {
                        hasRoared = true;
                        SetMonsterVisibleServerRpc(5f);
                        playDistantRoarServerRpc();

                    }
                    agent.speed = chaseSpeed;
                    isCounting = false;
                    if (targetPlayer != null)
                    {
                        SetMovingTowardsTargetPlayer(targetPlayer);
                    }
                    if (breakDoorTimer <= 0f && cachedDoors != null && currentBehaviourStateIndex==3 && base.IsOwner)
                    {
                        foreach (DoorLock door in cachedDoors) //loop through cached doors
                        {
                            if (door == null) continue;
                            AnimatedObjectTrigger animTrigger = door.GetComponent<AnimatedObjectTrigger>();
                            if (animTrigger != null && animTrigger.boolValue && !door.isLocked)
                            {
                                continue;
                            }
                            Transform t = door.transform;
                            for (int i = 0; i < 3 && t.parent != null; i++)
                            {
                                t = t.parent;
                            }
                            GameObject doorObj = t.gameObject;
                            if (!doorObj.GetComponent<Rigidbody>() && Vector3.Distance(transform.position, doorObj.transform.position) <= 4f)
                            {
                                breakDoorTimer = 1f;
                                Vector3 pushForce = (doorObj.transform.position - transform.position).normalized * 20f;
                                BreakDoorServerRpc(doorObj, pushForce);
                                break;
                            }
                        }
                    }
                    if (base.IsOwner)
                    {
                        if (targetPlayer != null && !targetPlayer.isInsideFactory && targetHasFled)
                        {
                            transform.position = currentPos;
                            agent.Warp(currentPos);
                            Debug.Log($"[NightTerrorAI] Warping: {currentPos}");
                            DestroyFakeDollServerRpc();
                            targetPlayer = null;
                            hasSummonedMonster = false;
                            targetHasFled = false;
                            manifestTimer = 0f;
                            isSeeking = false;
                            isFoundPhaseStarted = false;
                            creatureAnimator.SetBool("isSummoning", false);
                            ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
                            Debug.Log($"[NightTerrorAI] Ownership changed to host.");
                            SetMonsterInvisibleServerRpc(2f);
                        }
                    }
                    break;

            }
        }
        [ServerRpc(RequireOwnership = false)]
        public void StartCountingServerRpc()
        {
            StartCountingClientRpc();
        }

        [ClientRpc]
        public void StartCountingClientRpc()
        {
            StartCoroutine(CountingPhase());
        }

        [ServerRpc(RequireOwnership = false)]
        public void FoundPlayerServerRpc()
        {
            FoundPlayerClientRpc();
        }

        [ClientRpc]
        public void FoundPlayerClientRpc()
        {
            StartCoroutine(foundPhase());
        }



        [ServerRpc(RequireOwnership = false)]
        public void BreakDoorServerRpc(NetworkObjectReference netObjRef, Vector3 force)
        {
            BreakDoorClientRpc(netObjRef, force);
        }

        //Thanks to The Fiend code!
        [ClientRpc]
        public void BreakDoorClientRpc(NetworkObjectReference netObjRef, Vector3 force)
        {
            if (netObjRef.TryGet(out var networkObject))
            {
                GameObject door = networkObject.gameObject;
                Rigidbody rigidbody = door.AddComponent<Rigidbody>();
                AudioSource audioSource = door.AddComponent<AudioSource>(); //Add an audiosource to the door
                audioSource.PlayOneShot(doorSlam[enemyRandom.Next(0, doorSlam.Length)]); //Play a slam sound
                audioSource.spatialBlend = 1f;
                audioSource.maxDistance = 60f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
                audioSource.volume = 3f;
                StartCoroutine(TurnOffC(rigidbody, 0.12f)); //Turn of rigidbody so it doesnt fly everywhere
                rigidbody.AddForce(force, ForceMode.Impulse);
            }
        }

        private IEnumerator TurnOffC(Rigidbody rigidbody, float time)
        {
            rigidbody.detectCollisions = false;
            yield return new WaitForSeconds(time);
            rigidbody.detectCollisions = true;
            if (IsServer)
            {
                Destroy(rigidbody.gameObject, 5f); //Destroy door to clean up
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SpawnFakeDollServerRpc(Vector3 dollPos, Quaternion dollRot)
        {
            SpawnFakeDollClientRpc(dollPos, dollRot);
        }

        [ClientRpc]
        private void SpawnFakeDollClientRpc(Vector3 dollPos, Quaternion dollRot)
        {
            if (FakeDoll != null)
            {
                Destroy(FakeDoll);
            }
            FakeDoll = Instantiate(dollPrefab);
            FakeDoll.transform.position = dollPos;
            FakeDoll.transform.rotation = dollRot;


            Debug.Log("[NightTerrorAI] Doll spawned.");
        }

        [ServerRpc(RequireOwnership = false)]
        private void DestroyFakeDollServerRpc()
        {
            DestroyFakeDollClientRpc();
        }

        [ClientRpc]
        private void DestroyFakeDollClientRpc()
        {
            if (FakeDoll != null)
            {
                Destroy(FakeDoll);
                FakeDoll = null;
                Debug.Log("[NightTerrorAI] Doll destroyed.");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void BeginChaseServerRpc(int targetPlayerId, Vector3 dollPos, Quaternion dollRot, Vector3 monsterSpawnPos)
        {
            BeginChaseClientRpc(targetPlayerId, dollPos, dollRot, monsterSpawnPos);
        }

        [ClientRpc]
        private void BeginChaseClientRpc(int targetPlayerId, Vector3 dollPos, Quaternion dollRot, Vector3 monsterSpawnPos)
        {
            currentPos = dollPos;
            if (FakeDoll != null)
            {
                Destroy(FakeDoll);
            }
            FakeDoll = Instantiate(dollPrefab);
            FakeDoll.transform.position = dollPos;
            FakeDoll.transform.rotation = dollRot;

            transform.position = monsterSpawnPos;
            agent.Warp(monsterSpawnPos);
            isInDollForm = false;
            hasSummonedMonster = true;

            if (targetPlayerId >= 0 && targetPlayerId < StartOfRound.Instance.allPlayerScripts.Length)
            {
                targetPlayer = StartOfRound.Instance.allPlayerScripts[targetPlayerId];
                SetMovingTowardsTargetPlayer(targetPlayer);
            }

            SwitchToBehaviourStateOnLocalClient(3);
        }

        [ServerRpc(RequireOwnership = false)]
        private void AfterChaseCleanUpServerRpc()
        {
            AfterChaseCleanUpClientRpc();
        }
        [ClientRpc]
        private void AfterChaseCleanUpClientRpc()
        {
            agent.Warp(currentPos);
            SwitchToBehaviourStateOnLocalClient(0);
        }


        public override void OnCollideWithPlayer(Collider other)
        {
            if (chompCooldown < 2f) return;
            if (!other.gameObject.GetComponent<PlayerControllerB>())
            {
                return;
            }
            if (currentBehaviourStateIndex != 3) return;
            Debug.Log("[NightTerrorAI] Monster collided with player: " + other.gameObject.name);
            base.OnCollideWithPlayer(other);
            if (isEnemyDead)
            {
                return;
            }
            if (currentBehaviourStateIndex == 3)
            {
                Debug.Log("Night terror collided in monster form");
                PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
                if (playerControllerB != null)
                {
                    KillPlayerServerRpc((int)playerControllerB.playerClientId);
                    chompCooldown = 0f;
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void KillPlayerServerRpc(int playerId)
        {
            KillPlayerClientRpc(playerId);
        }

        [ClientRpc]
        public void KillPlayerClientRpc(int playerId)
        {
            KillPlayer(playerId);
        }

        private void KillPlayer (int playerId)
        {
            Debug.Log($"[NightTerrorAI] Killing: {playerId}");
            PlayerControllerB playerScript = StartOfRound.Instance.allPlayerScripts[playerId];
            playerScript.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Mauling);
            monsterAnimator.SetTrigger("attack");
            isFoundPhaseStarted = false;
            hasSummonedMonster = false;
            targetPlayer = null;
            creatureAnimator.SetBool("isSummoning", false);
            if (base.IsOwner)
            {
                SetMonsterInvisibleServerRpc(2f);
                DestroyFakeDollServerRpc();
                ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
            }
        }

        private IEnumerator CountingPhase()
        {
            if (base.IsOwner && wanderFacility !=null)
            {
                StopSearch(wanderFacility);
                agent.speed = 0f;
            }
            creatureSFX.Stop();
            creatureSFX.PlayOneShot(askHideSeekVoiceLines[enemyRandom.Next(0, askHideSeekVoiceLines.Length)]);
            Debug.Log($"[NightTerrorAI] Sitting down.");
            creatureAnimator.SetBool("isCounting", true);
            yield return new WaitForSeconds(3f);
            creatureSFX.Stop();
            creatureSFX.PlayOneShot(countVoiceLines[enemyRandom.Next(0, countVoiceLines.Length)]);
            Debug.Log($"[NightTerrorAI] Counting down from {countDuration}...");
            yield return new WaitForSeconds(countDuration);
            creatureAnimator.SetBool("isCounting", false);
            creatureSFX.Stop();
            creatureSFX.PlayOneShot(readytoFind[enemyRandom.Next(0, readytoFind.Length)]);
            targetPlayer = null;
            if (base.IsOwner)
            {
                isFoundPhaseStarted = false;
            }
            Debug.Log($"[NightTerrorAI] Ready or not, here I come!! xd");
            SwitchToBehaviourStateOnLocalClient(2);
        }

        private IEnumerator foundPhase()
        {
            if (seeking != null && seeking.inProgress)
            {
                StopSearch(seeking);
            }
            creatureAnimator.SetBool("isSummoning", true);
            agent.speed = 0f;
            Debug.Log($"[NightTerrorAI] Found you");
            creatureSFX.Stop();
            creatureSFX.PlayOneShot(foundVoiceLines[enemyRandom.Next(0, foundVoiceLines.Length)]);
            yield return new WaitForSeconds(foundPlayerTimer);
            Debug.Log($"[NightTerrorAI] Commencing chase.");

            currentPos = transform.position;
            currentRot = transform.rotation;
            if (base.IsOwner)
            {
                Transform farthestNode = base.ChooseFarthestNodeFromPosition(currentPos, true, 0);
                Vector3 spawnMonsterPos = farthestNode.position;
                if (targetPlayer != null)
                {
                    BeginChaseServerRpc((int)targetPlayer.playerClientId, currentPos, currentRot, spawnMonsterPos);
                    ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
                }
            }
            
            PlayerControllerB localPlayerController = GameNetworkManager.Instance.localPlayerController;
            if (localPlayerController ==targetPlayer)
            {
                HUDManager.Instance.UIAudio.PlayOneShot(chaseWindChimes[enemyRandom.Next(0, chaseWindChimes.Length)], 0.4f);
            }
            
        }

        [ServerRpc(RequireOwnership = false)]
        private void playDistantRoarServerRpc()
        {
            PlayDistantRoarClientRpc();
        }

        [ClientRpc]
        private void PlayDistantRoarClientRpc()
        {
            monsterDistant.PlayOneShot(distantRoars[enemyRandom.Next(0, distantRoars.Length)]);
        }

    }
}

