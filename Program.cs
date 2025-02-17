using Azure;
using Azure.AI.OpenAI;
using DotNetEnv;
using Microsoft.Extensions.AI;
using Pgvector;
using Npgsql;
using Azure.AI.Inference;

class Program
{
    static async Task Main(string[] args)
    {
        // Load environment variables
        Env.Load(".env");
        string githubKey = Env.GetString("GITHUB_KEY");
        string connectionString = Env.GetString("POSTGRES_CONNECTION_STRING");

        // Initialize PostgreSQL connection
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

    //     IChatClient client =
    // new AzureOpenAIClient(
    //     endpoint: new Uri("https://models.inference.ai.azure.com"),
    //     new AzureKeyCredential(githubKey))
    //     .AsChatClient("gpt-4o-mini");

        // Initialize embedding generator
        IEmbeddingGenerator<string, Embedding<float>> generator =
            new AzureOpenAIClient(
                new Uri("https://models.inference.ai.azure.com"),
                new AzureKeyCredential(githubKey))
                    .AsEmbeddingGenerator(modelId: "text-embedding-3-small");

        // Main execution
        await using var conn = await dataSource.OpenConnectionAsync();

        // Create the vector extension and movies table if they don't exist
        // await CandidateSearch.InitializeDatabase(conn);

        // Save movie vectors
        CandidateSearch candidateSearch = new CandidateSearch(generator);
        // await candidateSearch.StoreDocuments();

        // Perform multi-turn vector search
        while (true)
        {
            var query = "";
            Console.Write("Enter your search query (press Enter to quit): ");
            query = Console.ReadLine();
            if (string.IsNullOrEmpty(query))
            {
                break;
            }
            var queryEmbedding = await generator.GenerateEmbeddingVectorAsync(query);
            var result = await CandidateSearch.SearchVector(conn, queryEmbedding);

            if (result != null)
            {
                Console.WriteLine($"Candidate Name: {result.candidatename}");
                Console.WriteLine($"email: {result.email}");
                Console.WriteLine($"Skillset: {result.skillset}");
                Console.WriteLine($"Score: {result.Score}");
            }
            else
            {
                Console.WriteLine("No matching candidate found.");
            }
        }

        await conn.CloseAsync();
    }
}

