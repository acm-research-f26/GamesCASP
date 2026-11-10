using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class ChatGPTStrategy : MonoBehaviour, IAgentStrategy
{
    private bool waitingForReply;
    private float nextRequestTime;
    private string ApiKey = "null";

    private static readonly HttpClient Client = new HttpClient {Timeout = TimeSpan.FromSeconds(10)};
    private List<Fact> memories = new List<Fact>();

    private const string Instructions = @"
    You are part of AI strategy system that makes a decision every second in a zombie survival game. You are working together with a group of survivors to escape.
    All players are survivors until they are attacked by a zombie, then they become a half zombie. Half zombies must attack players every now and then or else they die.
    You are one of two roles, a survivor or a half zombie. As a survivor or a half zombie, your goal is to barricade window to prevent zombies from getting in
    and pull levers to win.
    If you are a survivor, reason about who might be a half zombie and run from them if you think that they are.
    If you are a half zombie, you must attack another survivor before your half zombie timer runs out to survive, however you are still trying
    to prevent zombies from getting in and you are still trying to escape. 
    Game Rules: Survivors have 3 health, Activate all levers to win (on = true means lever is already activated), Distance is in unity units, you should probably
    run from zombies once they are within 3 units of range, attack range is 1 unit, zombies move at 2 units per second, players move at 2.2 units per second. 

    Allowed actions: run_from, move_to_lever, pull_lever, hunt_lever,
    move_to_barricade, hold_barricade, leave_barricade, hunt_barricade,
    move_to_human, attack_human, wait.

    Targets must be IDs from current observations.
    Use an empty target for wait, hunt_lever, and hunt_barricade.
    Choose attack_human only when you are a half-zombie.
    
    Return only one JSON object containing action and target as strings.
    Example: {""action"":""hunt_lever"",""target"":""""}
    ";

    void Start()
    {
        EnvLoader.Load();
        ApiKey = EnvLoader.Get("GPT_KEY");
    }

    public void StoreFact(Fact fact)
    {
        memories.Add(fact);

        if (memories.Count > 5)
            memories.RemoveAt(0);
    }

    public async void DecideNextAction(Agent_Parent_Script agent)
    {
        if (waitingForReply || Time.time < nextRequestTime)
            return;

        nextRequestTime = Time.time + 1f;
        waitingForReply = true;

        try
        {
            var stats = AiDiagnostics.For(agent.name);
            stats.Requests++;
            float requestStartTime = Time.realtimeSinceStartup;
            Agent npc = agent.GetComponent<Agent>();
            AiAction actions = agent.GetComponent<AiAction>();
            LeverManager levers = FindAnyObjectByType<LeverManager>();

            string prompt = $"Health: {npc.Health}. Half-zombie: {npc.IsHalfZombie}.\n" + $"Interaction range: {actions.interactionRange}.\n";

            if (npc.IsHalfZombie) {
                float secondsLeft = Agent.BiteWindowSeconds -
                    (Time.time - npc.BiteWindowStartedAt);
                prompt += $"Bite seconds left: {secondsLeft:0.0}.\n";
            }
            if (levers != null) {
                prompt += $"levers activated: {levers.ActivatedLevers}" + $"/{levers.TotalLevers}.\n";
            }
            prompt += "\nCurrent observations:";

            foreach (Fact fact in agent.FetchTransientFacts()) {
                prompt +=
                    $"\n{fact.Type} id={fact.SubjectId}" +
                    $" distance={fact.Distance} on={fact.IsOn}" +
                    $" nearestZombie={fact.NearestZombieDistance}" +
                    $" barricadeHealth={fact.Reinforcement}" +
                    $" humanHealth={fact.Health}";
            }
            prompt += "\nRecent events:";

            foreach (Fact fact in memories)
            {
                prompt +=
                    $"\n{fact.Type} subject={fact.SubjectId}" +
                    $" target={fact.TargetId}";
            }

            prompt += $"\nMy ID: {npc.AgentId}";
            prompt += $"\ncurrent action: {actions.CurrentAction}";
            prompt += $"\nCurrent target: {actions.CurrentTarget}";

            var payload = new
            {
                model = "gpt-6-luna",
                reasoning_effort = "none",
                max_completion_tokens = 128,
                response_format = new { type = "json_object" },

                messages = new[]
                {
                    new { role = "system", content = Instructions },
                    new { role = "user", content = prompt }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post,"https://api.openai.com/v1/chat/completions");
            request.Headers.Add("Authorization", "Bearer " + ApiKey);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload),Encoding.UTF8,"application/json");
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync();
            JObject result = JObject.Parse(responseJson);
            string decisionJson = (string)result["choices"][0]["message"]["content"];
            JObject decision = JObject.Parse(decisionJson);

            Debug.Log($"{agent.name} ChatGPT reply: {decisionJson}", this);
            float roundTrip = Time.realtimeSinceStartup - requestStartTime;

            string action = (string)decision["action"];
            string target = (string)decision["target"] ?? "";

            if (agent != null &&
                isActiveAndEnabled &&
                ReferenceEquals(agent.ActiveStrategy, this))
            {
                stats.Responses++;
                stats.RoundTripMs.Add(roundTrip * 1000f);
                agent.ExecuteAction(action, target);
                ExperimentRunner.ActiveMeasurements?.AddLlmActionLatency(roundTrip);
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("OpenAI decision failed: " + error.Message);
        }
        finally
        {
            waitingForReply = false;
        }
    }
}