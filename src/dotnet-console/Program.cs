// Copyright (c) Microsoft. All rights reserved.
#pragma warning disable OPENAI001 // Suppress experimental API warnings for Responses API usage.
#pragma warning disable MAAI001  // Suppress experimental API warnings for Agents AI experiments.

using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using dotenv.net;
using System.ClientModel.Primitives;
using System.Text.Json;

DotEnv.Load();

var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4";

const int MaxContextWindowTokens = 1_050_000;
const int MaxOutputTokens = 128_000;
const string TracingSourceName = "Harness.SubAgents";

// chatClient is any IChatClient implementation (Foundry, Azure OpenAI, OpenAI, Anthropic, ...).
var chatClient = new AIProjectClient(
    new Uri(endpoint),
    new DefaultAzureCredential(),
    new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(3) })
    .GetProjectOpenAIClient()
    .GetResponsesClient()
    .AsIChatClient(deploymentName);

//version 1
//AIAgent agent = chatClient.AsHarnessAgent();

//version 2
AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
{
    Name = "market-agent",
    HarnessInstructions = "Use tools deliberately and report verified results.",
    ChatOptions = new ChatOptions
    {
        Instructions = "You are a research assistant focused on market data.",
        Tools = [/* your custom AIFunction tools */],
    },
    MaxContextWindowTokens = MaxContextWindowTokens,
    MaxOutputTokens = MaxOutputTokens,
});

// A session carries the harness state (plan, todos, history) across turns.
AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine("Harness agent ready. Type 'exit' to quit.");
while (true)
{
    Console.Write("> ");
    string? input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    // Stream this turn's output as the harness plans and works through the request.
    await foreach (var update in agent.RunStreamingAsync(input, session))
    {
        if (update.Text != "")
        {
            Console.Write(update);
            LogStreamingMetadata(update);
        }
    }

    Console.WriteLine();
}

static void LogStreamingMetadata(object? update)
{
    if (update is not AgentResponseUpdate responseUpdate)
    {
        return;
    }

    foreach (var toolCall in responseUpdate.Contents.OfType<FunctionCallContent>())
    {
        Console.WriteLine($"[Tool used: {toolCall.Name} ({toolCall.CallId})]");
    }
}
