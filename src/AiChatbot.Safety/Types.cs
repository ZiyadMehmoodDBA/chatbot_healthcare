namespace AiChatbot.Safety;

// Local: runs on infrastructure we control (Ollama). External: data leaves the network (OpenAI and compatibles).
public enum ProviderKind { Local, External }

// What a piece of context is, so the gate can decide who may see it
public enum DataClass { Public, Aggregate, PatientLevel }
