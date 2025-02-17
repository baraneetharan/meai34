# Building a Candidate Search System with Vector Embeddings and PostgreSQL

In this blog, we will explore how to build a candidate search system using **vector embeddings**, **PostgreSQL** (with the `pgvector` extension), and **Azure OpenAI**. This system allows you to extract information from PDF resumes, generate vector embeddings for them, store the data in a PostgreSQL database, and perform similarity searches to find candidates based on specific queries.

---

## Table of Contents
1. [Introduction](#introduction)
2. [Key Components of the System](#key-components-of-the-system)
3. [Code Walkthrough](#code-walkthrough)
   - [Environment Setup](#environment-setup)
   - [Database Initialization](#database-initialization)
   - [Extracting Candidate Details](#extracting-candidate-details)
   - [Generating Embeddings](#generating-embeddings)
   - [Storing Data in PostgreSQL](#storing-data-in-postgresql)
   - [Performing Similarity Searches](#performing-similarity-searches)
4. [How It Works](#how-it-works)
5. [Use Cases](#use-cases)
6. [Conclusion](#conclusion)

---

## Introduction

The goal of this project is to create a system that can:
1. Parse PDF resumes and extract relevant candidate details (e.g., name, email, skills, experience).
2. Generate **vector embeddings** for the extracted text using Azure OpenAI's embedding models.
3. Store the candidate details and their embeddings in a PostgreSQL database with the `pgvector` extension.
4. Perform similarity searches to find candidates whose profiles match a given query.

This approach leverages **natural language processing (NLP)** and **vector-based search** to enable efficient and accurate candidate retrieval.

---

## Key Components of the System

### 1. **PDF Parsing**
   - The system uses the `iText` library to extract text from PDF resumes.
   - Extracted text is processed to identify key candidate details such as name, email, skills, and experience.

### 2. **Azure OpenAI Integration**
   - Azure OpenAI is used to:
     - Extract structured candidate details from unstructured text.
     - Generate vector embeddings for the extracted text.

### 3. **PostgreSQL with pgvector**
   - PostgreSQL is used as the database to store candidate details and their embeddings.
   - The `pgvector` extension enables efficient storage and querying of vector embeddings.

### 4. **Vector-Based Search**
   - The system performs similarity searches by calculating the distance between query embeddings and stored embeddings using cosine similarity.

---

## Code Walkthrough

### Environment Setup

The first step is to load environment variables and initialize the necessary components:

```csharp
Env.Load(".env");
string githubKey = Env.GetString("GITHUB_KEY");
string connectionString = Env.GetString("POSTGRES_CONNECTION_STRING");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

IEmbeddingGenerator<string, Embedding<float>> generator =
    new AzureOpenAIClient(
        new Uri("https://models.inference.ai.azure.com"),
        new AzureKeyCredential(githubKey))
            .AsEmbeddingGenerator(modelId: "text-embedding-3-small");
```

- **Environment Variables**: The `.env` file contains sensitive information like the GitHub API key and PostgreSQL connection string.
- **PostgreSQL DataSource**: The `NpgsqlDataSourceBuilder` is configured to use the `pgvector` extension for vector storage.
- **Azure OpenAI Client**: The `AzureOpenAIClient` is initialized to interact with Azure OpenAI for generating embeddings.

---

### Database Initialization

The `CandidateSearch` class includes logic to create a table in PostgreSQL if it doesn't already exist:

```sql
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
);
```

- This table stores candidate details along with their vector embeddings (`vector REAL[]`).

---

### Extracting Candidate Details

The `ExtractCandidateDetails` method sends the extracted text to Azure OpenAI to extract structured candidate details:

```csharp
private async Task<Dictionary<string, object>> ExtractCandidateDetails(string text)
{
    string request = $"Extract the following details from the given text and return the result as JSON:\n\n1. Candidate Name\n2. Email\n3. Contact Number\n4. Academics\n5. Experience\n6. Certification\n7. Address\n8. Projects\n9. Internship\n10. Skillset\n11. Programming Languages\n12. Spoken Languages\n13. Summary\n\nText:\n{text}.\n\nIn the 'Summary' column, provide a comprehensive overview of the {text}.";
    var response = await GetLLMResponseAsync(request);

    try
    {
        var details = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Trim().Trim('`').Trim("json".ToCharArray()).Trim());
        return details;
    }
    catch (JsonReaderException ex)
    {
        Console.WriteLine($"Error deserializing JSON: {ex.Message}");
        return new Dictionary<string, object>();
    }
}
```

- The method constructs a prompt asking Azure OpenAI to extract specific fields from the resume text.
- The response is parsed into a dictionary for further processing.

---

### Generating Embeddings

The `GenerateEmbeddingVectorAsync` method generates vector embeddings for the extracted text:

```csharp
var documentEmbeddingVector = await _embeddingGenerator.GenerateEmbeddingVectorAsync(combinedText.ToString());
```

- These embeddings are numerical representations of the text, enabling similarity comparisons.

---

### Storing Data in PostgreSQL

The extracted candidate details and their embeddings are stored in the `docvectors` table:

```csharp
using (var cmd = new NpgsqlCommand(@"
INSERT INTO docvectors (filename, candidatename, email, contactnumber, academics, experience, certification, address, projects, internship, skillset, programming_languages, spoken_languages, summary, vector)
VALUES (@filename, @candidatename, @email, @contactnumber, @academics, @experience, @certification, @address, @projects, @internship, @skillset, @programming_languages, @spoken_languages, @summary, @vector)", conn))
{
    cmd.Parameters.AddWithValue("filename", fileName);
    cmd.Parameters.AddWithValue("candidatename", candidateDetails["Candidate Name"]?.ToString() ?? "N/A");
    cmd.Parameters.AddWithValue("email", candidateDetails["Email"]?.ToString() ?? "N/A");
    // ... other parameters ...
    cmd.Parameters.AddWithValue("vector", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Real, documentEmbeddingVector.ToArray());
    cmd.ExecuteNonQuery();
}
```

- Each record includes both structured data (e.g., name, email) and the vector embedding.

---

### Performing Similarity Searches

The `SearchVector` method performs a similarity search using the `pgvector` extension:

```sql
SELECT candidatename, email, skillset, vector::vector <-> @queryVector AS score
FROM docvectors
ORDER BY vector::vector <-> @queryVector
LIMIT 1
```

- The `<->` operator calculates the distance between the query embedding and stored embeddings.
- Results are ordered by similarity, and the top match is returned.

---

## How It Works

1. **Resume Parsing**: PDF resumes are parsed to extract raw text.
2. **Detail Extraction**: Azure OpenAI extracts structured details (e.g., name, skills) from the text.
3. **Embedding Generation**: A vector embedding is generated for the extracted text.
4. **Data Storage**: Candidate details and embeddings are stored in PostgreSQL.
5. **Query Processing**: User queries are converted into embeddings, and similarity searches are performed to find matching candidates.

---

## Use Cases

1. **Recruitment Platforms**: Automate candidate screening by matching resumes to job descriptions.
2. **Talent Management**: Identify employees with specific skills for internal mobility or training programs.
3. **Knowledge Management**: Organize and retrieve documents based on content similarity.

---

## Conclusion

This system demonstrates the power of combining **NLP**, **vector embeddings**, and **PostgreSQL** to build an intelligent candidate search solution. By leveraging Azure OpenAI and the `pgvector` extension, you can efficiently process and query large volumes of unstructured data. Whether you're building a recruitment platform or a talent management system, this approach offers scalability, accuracy, and flexibility.

Feel free to experiment with the code and adapt it to your specific use case!
