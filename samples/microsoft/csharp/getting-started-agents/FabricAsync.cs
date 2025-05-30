using Azure.AI.Agents.Persistent;
using Azure.Identity;
using System;
using Microsoft.Extensions.Configuration;
using Azure;
using System.Threading.Tasks;

// Get Connection information from App Configuration
IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var projectEndpoint = configuration["ProjectEndpoint"];
var modelDeploymentName = configuration["ModelDeploymentName"];
var fabricConnectionId = configuration["FabricConnectionId"];

// Create the Agent Client
PersistentAgentsClient agentClient = new(
    projectEndpoint,
    new DefaultAzureCredential(),
    new PersistentAgentsAdministrationClientOptions(
        PersistentAgentsAdministrationClientOptions.ServiceVersion.V2025_05_01
    ));

// Create the MicrosoftFabricToolDefinition object needed when creating the agent
ToolConnectionList connectionList = new()
{
    ConnectionList = { new ToolConnection(fabricConnectionId) }
};
MicrosoftFabricToolDefinition fabricTool = new(connectionList);

// Create the Agent
PersistentAgent agent = await agentClient.Administration.CreateAgentAsync(
    model: modelDeploymentName,
    name: "my-assistant",
    instructions: "You are a helpful assistant.",
    tools: [fabricTool]);

PersistentAgentThread thread = await agentClient.Threads.CreateThreadAsync();

// Create message and run the agent
ThreadMessage message = await agentClient.Messages.CreateMessageAsync(
    thread.Id,
    MessageRole.User,
    "What are the top 3 weather events with highest property damage?");
ThreadRun run = agentClient.Runs.CreateRun(thread, agent);

// Wait for the agent to finish running
do
{
    await Task.Delay(TimeSpan.FromMilliseconds(500));
    run = await agentClient.Runs.GetRunAsync(thread.Id, run.Id);
}
while (run.Status == RunStatus.Queued
    || run.Status == RunStatus.InProgress);

// Confirm that the run completed successfully
if (run.Status != RunStatus.Completed)
{
    throw new Exception("Run did not complete successfully, error: " + run.LastError?.Message);
}

// Retrieve all messages from the agent client
AsyncPageable<ThreadMessage> messages = agentClient.Messages.GetMessagesAsync(
    threadId: thread.Id,
    order: ListSortOrder.Ascending
);

// Process messages in order
await foreach (ThreadMessage threadMessage in messages)
{
    Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");
    foreach (MessageContent contentItem in threadMessage.ContentItems)
    {
        if (contentItem is MessageTextContent textItem)
        {
            string response = textItem.Text;

            // If we have Text URL citation annotations, reformat the response to show title & URL for citations
            if (textItem.Annotations != null)
            {
                foreach (MessageTextAnnotation annotation in textItem.Annotations)
                {
                    if (annotation is MessageTextUrlCitationAnnotation urlAnnotation)
                    {
                        response = response.Replace(urlAnnotation.Text, $" [{urlAnnotation.UrlCitation.Title}]({urlAnnotation.UrlCitation.Url})");
                    }
                }
            }
            Console.Write($"Agent response: {response}");
        }
        else if (contentItem is MessageImageFileContent imageFileItem)
        {
            Console.Write($"<image from ID: {imageFileItem.FileId}");
        }
        Console.WriteLine();
    }
}

// Delete thread and agent
await agentClient.Threads.DeleteThreadAsync(threadId: thread.Id);
await agentClient.Administration.DeleteAgentAsync(agentId: agent.Id);
