using System;
using System.Collections.Generic;
using RPA.AiAgent.Models;

namespace AiRpaStudio.Tests;

public class AiAgentModelsTests
{
    [Test]
    public void AiModel_DefaultSizeFormatted_IsEmpty()
    {
        var m = new AiModel();
        Assert.That(m.SizeFormatted, Is.EqualTo(string.Empty));
    }

    [Test]
    public void AiModel_ToString_WithNoName_IsNull()
    {
        var m = new AiModel();
        Assert.That(m.ToString(), Is.Null);
    }

    [Test]
    public void AiModel_SizeFormatted_FormatsBytes()
    {
        Assert.That(new AiModel { Size = 0 }.SizeFormatted, Is.EqualTo("0 B"));
        Assert.That(new AiModel { Size = 1024 }.SizeFormatted, Is.EqualTo("1 KB"));
        Assert.That(new AiModel { Size = 1536 }.SizeFormatted, Is.EqualTo(string.Format("{0:0.##} KB", 1.5)));
        Assert.That(new AiModel { Size = 1048576 }.SizeFormatted, Is.EqualTo("1 MB"));
    }

    [Test]
    public void AiModel_ToString_UsesDisplayNameOverName()
    {
        Assert.That(new AiModel { Name = "gpt-4" }.ToString(), Is.EqualTo("gpt-4"));
        var m = new AiModel { DisplayName = "GPT", Name = "gpt-4", Size = 1048576 };
        Assert.That(m.ToString(), Is.EqualTo("GPT (1 MB)"));
        m.Size = null;
        Assert.That(m.ToString(), Is.EqualTo("GPT"));
    }

    [Test]
    public void ChatMessage_DefaultCtor_SetsDefaults()
    {
        var msg = new ChatMessage();
        Assert.That(msg.Content, Is.EqualTo(string.Empty));
        Assert.That(msg.Type, Is.EqualTo(MessageType.Normal));
        Assert.That(msg.IsStreaming, Is.False);
        Assert.That(msg.Role, Is.Null);
        Assert.That(msg.Timestamp, Is.EqualTo(DateTime.Now).Within(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void ChatMessage_Content_RaisesPropertyChanged()
    {
        var msg = new ChatMessage();
        var events = new List<string>();
        msg.PropertyChanged += (s, e) => events.Add(e.PropertyName);
        msg.Content = "hi";
        Assert.That(events, Does.Contain("Content"));
    }

    [Test]
    public void ChatMessage_SameContent_DoesNotRaiseEvent()
    {
        var msg = new ChatMessage { Content = "hi" };
        int count = 0;
        msg.PropertyChanged += (s, e) => count++;
        msg.Content = "hi";
        Assert.That(count, Is.Zero);
    }

    [Test]
    public void ChatMessage_Type_RaisesPropertyChanged()
    {
        var msg = new ChatMessage();
        var events = new List<string>();
        msg.PropertyChanged += (s, e) => events.Add(e.PropertyName);
        msg.Type = MessageType.System;
        Assert.That(events, Does.Contain("Type"));
    }

    [Test]
    public void ChatMessage_Role_IsPlainProperty()
    {
        var msg = new ChatMessage();
        int count = 0;
        msg.PropertyChanged += (s, e) => count++;
        msg.Role = "user";
        Assert.That(count, Is.Zero);
    }

    [Test]
    public void Conversation_DefaultCtor_SetsDefaults()
    {
        var conv = new Conversation();
        Assert.That(conv.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(conv.Title, Is.EqualTo("Новый диалог"));
        Assert.That(conv.Messages, Is.Not.Null);
        Assert.That(conv.Messages, Is.Empty);
        Assert.That(conv.ModelName, Is.Null);
    }

    [Test]
    public void MessageContentPart_DefaultCtor_SetsDefaults()
    {
        var p = new MessageContentPart();
        Assert.That(p.Type, Is.EqualTo(ContentPartType.Text));
        Assert.That(p.Content, Is.Null);
        Assert.That(p.Language, Is.Null);
    }

    [Test]
    public void ContentPartType_Values()
    {
        Assert.That((int)ContentPartType.Text, Is.EqualTo(0));
        Assert.That((int)ContentPartType.CodeBlock, Is.EqualTo(1));
    }

    [Test]
    public void McpToolPermission_Default_IsNotApproved()
    {
        var perm = new McpToolPermission();
        Assert.That(perm.IsApproved, Is.False);
    }

    [Test]
    public void McpToolPermission_IsApproved_RaisesPropertyChanged()
    {
        var perm = new McpToolPermission();
        string changed = null;
        perm.PropertyChanged += (s, e) => changed = e.PropertyName;
        perm.IsApproved = true;
        Assert.That(changed, Is.EqualTo("IsApproved"));
        Assert.That(perm.IsApproved, Is.True);
    }

    [Test]
    public void AllProviderSettings_Default_HasEmptyDictionary()
    {
        var all = new AllProviderSettings();
        Assert.That(all.Providers, Is.Not.Null);
        Assert.That(all.Providers, Is.Empty);
    }

    [Test]
    public void AllProviderSettings_GetOrSetSelectedModel()
    {
        var all = new AllProviderSettings();
        Assert.That(all.GetSelectedModelId(ProviderType.Ollama), Is.Null);
        all.SetSelectedModel(ProviderType.Ollama, "llama3");
        Assert.That(all.GetSelectedModelId(ProviderType.Ollama), Is.EqualTo("llama3"));
        Assert.That(all.Providers.Count, Is.EqualTo(1));
        Assert.That(all.GetSelectedModelId(ProviderType.OpenRouter), Is.Null);
    }

    [Test]
    public void ProviderType_Values()
    {
        Assert.That((int)ProviderType.Ollama, Is.EqualTo(0));
        Assert.That((int)ProviderType.OpenRouter, Is.EqualTo(1));
    }

    [Test]
    public void OllamaModel_DisplayName_EqualsName()
    {
        var m = new OllamaModel { Name = "llama3", Size = 1024 };
        Assert.That(m.DisplayName, Is.EqualTo("llama3"));
        Assert.That(m.SizeFormatted, Is.EqualTo("1 KB"));
        Assert.That(m.ToString(), Is.EqualTo("llama3 (1 KB)"));
    }

    [Test]
    public void OllamaResponse_Deserializes()
    {
        string json = "{\"model\":\"llama3\",\"message\":{\"role\":\"assistant\",\"content\":\"hi\"},\"done\":true}";
        var resp = Newtonsoft.Json.JsonConvert.DeserializeObject<OllamaResponse>(json);
        Assert.That(resp.Model, Is.EqualTo("llama3"));
        Assert.That(resp.Message.Role, Is.EqualTo("assistant"));
        Assert.That(resp.Message.Content, Is.EqualTo("hi"));
        Assert.That(resp.Done, Is.True);
    }

    [Test]
    public void OllamaModelsResponse_Deserializes()
    {
        string json = "{\"models\":[{\"name\":\"llama3\",\"modified_at\":\"2024-01-01T00:00:00Z\",\"size\":1024,\"digest\":\"abc\",\"details\":{\"format\":\"gguf\",\"family\":\"llama\",\"parameter_size\":\"8B\",\"quantization_level\":\"Q4_K_M\"}}]}";
        var resp = Newtonsoft.Json.JsonConvert.DeserializeObject<OllamaModelsResponse>(json);
        Assert.That(resp.Models[0].Name, Is.EqualTo("llama3"));
        Assert.That(resp.Models[0].Size, Is.EqualTo(1024));
        Assert.That(resp.Models[0].Details.Format, Is.EqualTo("gguf"));
        Assert.That(resp.Models[0].Details.QuantizationLevel, Is.EqualTo("Q4_K_M"));
    }

    [Test]
    public void OpenRouterChatRequest_SerializesWireFormat()
    {
        var req = new OpenRouterChatRequest
        {
            Model = "gpt",
            Stream = true,
            Temperature = 0.7,
            TopP = 0.9,
            MaxTokens = 100,
            Messages = new[] { new OpenRouterMessage { Role = "user", Content = "hi" } }
        };
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(req);
        var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
        Assert.That((string)obj["model"], Is.EqualTo("gpt"));
        Assert.That((bool)obj["stream"], Is.True);
        Assert.That((double)obj["temperature"], Is.EqualTo(0.7));
        Assert.That((double)obj["top_p"], Is.EqualTo(0.9));
        Assert.That((int)obj["max_tokens"], Is.EqualTo(100));
    }

    [Test]
    public void OpenRouterChatResponse_Deserializes()
    {
        string json = "{\"id\":\"1\",\"model\":\"gpt\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"hi\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";
        var resp = Newtonsoft.Json.JsonConvert.DeserializeObject<OpenRouterChatResponse>(json);
        Assert.That(resp.Id, Is.EqualTo("1"));
        Assert.That(resp.Choices[0].Message.Content, Is.EqualTo("hi"));
        Assert.That(resp.Usage.PromptTokens, Is.EqualTo(10));
        Assert.That(resp.Usage.CompletionTokens, Is.EqualTo(5));
        Assert.That(resp.Usage.TotalTokens, Is.EqualTo(15));
    }
}
