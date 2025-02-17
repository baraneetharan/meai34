using System.Text;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.OpenAI;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Pgvector;

public class CandidateSearch
{


    private readonly IChatClient _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private string folderPath = @"D:\baraneetharan\myworks\meai\meai34\6documents";

    public CandidateSearch(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }


    public async Task StoreDocuments()
    {


        string connString = "Host=localhost;Username=postgres;Password=Kgisl@12345;Database=meai34";
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        var conn = dataSource.OpenConnection();

        // Check if table exists and create if it doesn't
        using (var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS docvectors (
                id SERIAL PRIMARY KEY,
                filename TEXT,
                candidatename TEXT,
                email TEXT,
                contactnumber TEXT,
                academics TEXT,
                experience TEXT,
                certification TEXT,
                address TEXT,
                projects TEXT,
                internship TEXT,
                skillset TEXT,
                programming_languages TEXT,
                spoken_languages TEXT,
                summary TEXT,
                vector REAL[] NOT NULL
            )", conn))
        {
            cmd.ExecuteNonQuery();
        }

        string[] pdfFiles = Directory.GetFiles(folderPath, "*.pdf");

        if (pdfFiles.Length == 0)
        {
            Console.WriteLine("No PDF files found in the specified folder.");
            return;
        }

        foreach (string pdfPath in pdfFiles)
        {
            var combinedText = new StringBuilder();
            string fileName = Path.GetFileName(pdfPath);

            Console.WriteLine($"Processing file: {fileName}");

            using (PdfReader reader = new PdfReader(pdfPath))
            using (PdfDocument pdfDoc = new PdfDocument(reader))
            {
                for (int page = 1; page <= pdfDoc.GetNumberOfPages(); page++)
                {
                    ITextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                    string currentText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(page), strategy);
                    // Console.WriteLine(currentText);

                    combinedText.Append(currentText).Append(" ");
                }
            }

            var candidateDetails = await ExtractCandidateDetails(combinedText.ToString());
            Console.WriteLine("Candidate Details:");
            Console.WriteLine($"Candidate Name: {candidateDetails["Candidate Name"]}");
            Console.WriteLine($"Email: {candidateDetails["Email"]}");
            Console.WriteLine($"Contact Number: {candidateDetails["Contact Number"]}");
            Console.WriteLine($"Academics: {candidateDetails["Academics"]}");
            Console.WriteLine($"Experience: {candidateDetails["Experience"]}");
            Console.WriteLine($"Certification: {candidateDetails["Certification"]}");
            Console.WriteLine($"Address: {candidateDetails["Address"]}");
            Console.WriteLine($"Projects: {candidateDetails["Projects"]}");
            Console.WriteLine($"Internship: {candidateDetails["Internship"]}");
            Console.WriteLine($"Skillset: {candidateDetails["Skillset"]}");
            Console.WriteLine($"Programming Languages: {candidateDetails["Programming Languages"]}");
            Console.WriteLine($"Spoken Languages: {candidateDetails["Spoken Languages"]}");
            Console.WriteLine($"Summary: {candidateDetails["Summary"]}");

            try
            {
                // Generate embedding for the extracted details
                var documentEmbeddingVector = await _embeddingGenerator.GenerateEmbeddingVectorAsync(combinedText.ToString());
                Console.WriteLine("Generated embedding for the combined document.");

                // Store the candidate details and vector in the database
                using (var cmd = new NpgsqlCommand(@"
                    INSERT INTO docvectors (filename, candidatename, email, contactnumber, academics, experience, certification, address, projects, internship, skillset, programming_languages, spoken_languages, summary, vector)
                    VALUES (@filename, @candidatename, @email, @contactnumber, @academics, @experience, @certification, @address, @projects, @internship, @skillset, @programming_languages, @spoken_languages, @summary, @vector)", conn))
                {
                    cmd.Parameters.AddWithValue("filename", fileName);
                    cmd.Parameters.AddWithValue("candidatename", candidateDetails["Candidate Name"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("email", candidateDetails["Email"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("contactnumber", candidateDetails["Contact Number"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("academics", JsonConvert.SerializeObject(candidateDetails["Academics"]) ?? "N/A");
                    cmd.Parameters.AddWithValue("experience", candidateDetails["Experience"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("certification", candidateDetails["Certification"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("address", candidateDetails["Address"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("projects", JsonConvert.SerializeObject(candidateDetails["Projects"]) ?? "N/A");
                    cmd.Parameters.AddWithValue("internship", candidateDetails["Internship"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("skillset", JsonConvert.SerializeObject(candidateDetails["Skillset"]) ?? "N/A");
                    cmd.Parameters.AddWithValue("programming_languages", JsonConvert.SerializeObject(candidateDetails["Programming Languages"]) ?? "N/A");
                    cmd.Parameters.AddWithValue("spoken_languages", candidateDetails["Spoken Languages"]?.ToString() ?? "N/A");
                    cmd.Parameters.AddWithValue("summary", candidateDetails["Summary"]?.ToString() ?? "N/A");
                    // // Create summary by concatenating all other fields
                    // StringBuilder summaryBuilder = new StringBuilder();
                    // foreach (var key in candidateDetails.Keys)
                    // {
                    //     summaryBuilder.Append($"{key}: {candidateDetails[key]?.ToString() ?? "N/A"}\n");
                    // }
                    // cmd.Parameters.AddWithValue("summary", summaryBuilder.ToString());
                    cmd.Parameters.AddWithValue("vector", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Real, documentEmbeddingVector.ToArray());

                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine($"Stored embedding for file: {fileName}");
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"HTTP request error: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating embeddings or storing in DB: {ex.Message}");
            }
        }

        Console.WriteLine("All PDF files processed and embeddings stored.");
    }

    private async Task<Dictionary<string, dynamic>> ExtractCandidateDetails(string text)
    {
        // Request specific details from LLM
        string request = $"Extract the following details from the given text and return the result as JSON:\n\n1. Candidate Name\n2. Email\n3. Contact Number\n4. Academics\n5. Experience\n6. Certification\n7. Address\n8. Projects\n9. Internship\n10. Skillset\n11. Programming Languages\n12. Spoken Languages\n13. Summary\n\nText:\n{{text}}.\n\nIn the 'Summary' column, provide a comprehensive overview of the {text}.";


        var response = await GetLLMResponseAsync(request);
        // Parse the JSON response 
        try
        {
            if (string.IsNullOrEmpty(response.Trim().Trim('`').Trim("json".ToCharArray()).Trim()))
            {
                Console.WriteLine("LLM response is null or empty.");
                return new Dictionary<string, dynamic>(); // Return an empty dictionary
            }

            // Attempt to parse the JSON response
            try
            {
                var details = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(response.Trim().Trim('`').Trim("json".ToCharArray()).Trim());
                return details;
            }
            catch (JsonReaderException ex)
            {
                Console.WriteLine($"Error deserializing JSON: {ex.Message}");
                Console.WriteLine($"Raw LLM Response: {response.Trim().Trim('`').Trim("json".ToCharArray()).Trim()}"); // Log the raw response for debugging

                // Attempt to fix the JSON by adding curly braces if they are missing
                if (!response.Trim().StartsWith("{"))
                {
                    response = "{" + response;
                }
                if (!response.Trim().EndsWith("}"))
                {
                    response = response + "}";
                }

                // Attempt to deserialize again after fixing
                try
                {
                    var details = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(response.Trim().Trim('`').Trim("json".ToCharArray()).Trim());
                    Console.WriteLine("JSON deserialized successfully after fixing.");
                    return details;
                }
                catch (JsonReaderException ex2)
                {
                    Console.WriteLine($"Error deserializing JSON after fixing: {ex2.Message}");
                    Console.WriteLine($"Raw LLM Response after fixing: {response.Trim().Trim('`').Trim("json".ToCharArray()).Trim()}");
                    return new Dictionary<string, dynamic>(); // Return an empty dictionary
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            return new Dictionary<string, dynamic>(); // Return an empty dictionary
        }
    }

    private async Task<string> GetLLMResponseAsync(string input)
    {
        IChatClient openAIClient = new AzureOpenAIClient(
        endpoint: new Uri("https://models.inference.ai.azure.com"),
        new AzureKeyCredential(githubkey))
        .AsChatClient("gpt-4o-mini");

        // Placeholder implementation for interacting with the LLM
        // Replace with actual code to call the LLM API and get the response
        var response = openAIClient.CompleteAsync(input);
        // Console.WriteLine($"Assistant -> {response.Result.Message.Text}");
        // return await Task.FromResult($"Response from LLM for input: {input}");
        return response.Result.Message.Text;
    }

    public static async Task<SearchResult?> SearchVector(NpgsqlConnection conn, ReadOnlyMemory<float> queryEmbedding)
    {
        // var command = new NpgsqlCommand("SELECT candidatename FROM candidates 
        // WHERE vector <-> @queryVector < 0.5", connection);
        // command.Parameters.AddWithValue("queryVector", queryVector);

        await using (var command = new NpgsqlCommand(@"
            SELECT candidatename, email, skillset, vector::vector <-> @queryVector AS score
            FROM docvectors
            ORDER BY vector::vector <-> @queryVector
            LIMIT 1", conn))
        {
            command.Parameters.Add(new NpgsqlParameter("queryVector", new Vector(queryEmbedding.ToArray())));
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    return new SearchResult
                    {
                        candidatename = reader.GetString(0),
                        email = reader.GetString(1),
                        skillset = reader.GetString(2),
                        Score = reader.GetDouble(3)
                    };
                }
            }
        }

        return null;
    }
}