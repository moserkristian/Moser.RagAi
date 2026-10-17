using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Moser.RagAi;

public sealed class PgDocumentIndex : IDocumentIndex
{
    private readonly NpgsqlDataSource _dataSource;

    public PgDocumentIndex(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS documents (
                id uuid PRIMARY KEY,
                title text NOT NULL,
                file_name text NOT NULL,
                is_sample boolean NOT NULL,
                uploaded_by text NOT NULL,
                uploaded_at timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chunks (
                id uuid PRIMARY KEY,
                document_id uuid NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
                ordinal integer NOT NULL,
                page_number integer NULL,
                content text NOT NULL,
                embedding vector(384) NOT NULL
            );
            CREATE INDEX IF NOT EXISTS chunks_embedding_hnsw
                ON chunks USING hnsw (embedding vector_cosine_ops);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await connection.ReloadTypesAsync(cancellationToken);
    }

    public async Task<StoredDocument> AddAsync(DocumentDraft draft, CancellationToken cancellationToken)
    {
        var document = new StoredDocument(
            Guid.NewGuid(),
            draft.Title,
            draft.FileName,
            draft.IsSample,
            draft.UploadedBy,
            DateTimeOffset.UtcNow);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var insertDocument = connection.CreateCommand())
        {
            insertDocument.Transaction = transaction;
            insertDocument.CommandText =
                """
                INSERT INTO documents (id, title, file_name, is_sample, uploaded_by, uploaded_at)
                VALUES (@id, @title, @file_name, @is_sample, @uploaded_by, @uploaded_at)
                """;
            insertDocument.Parameters.AddWithValue("id", document.Id);
            insertDocument.Parameters.AddWithValue("title", document.Title);
            insertDocument.Parameters.AddWithValue("file_name", document.FileName);
            insertDocument.Parameters.AddWithValue("is_sample", document.IsSample);
            insertDocument.Parameters.AddWithValue("uploaded_by", document.UploadedBy);
            insertDocument.Parameters.AddWithValue("uploaded_at", document.UploadedAt);
            await insertDocument.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var chunk in draft.Chunks)
        {
            if (chunk.Embedding.Length != MiniLmEmbedder.Dimensions)
                throw new InvalidOperationException($"Expected a {MiniLmEmbedder.Dimensions}-dimension embedding.");

            await using var insertChunk = connection.CreateCommand();
            insertChunk.Transaction = transaction;
            insertChunk.CommandText =
                """
                INSERT INTO chunks (id, document_id, ordinal, page_number, content, embedding)
                VALUES (@id, @document_id, @ordinal, @page_number, @content, @embedding)
                """;
            insertChunk.Parameters.AddWithValue("id", Guid.NewGuid());
            insertChunk.Parameters.AddWithValue("document_id", document.Id);
            insertChunk.Parameters.AddWithValue("ordinal", chunk.Ordinal);
            insertChunk.Parameters.Add(new NpgsqlParameter("page_number", NpgsqlDbType.Integer)
            {
                Value = chunk.PageNumber.HasValue ? chunk.PageNumber.Value : DBNull.Value
            });
            insertChunk.Parameters.AddWithValue("content", chunk.Text);
            insertChunk.Parameters.Add(new NpgsqlParameter("embedding", new Vector(chunk.Embedding))
            {
                DataTypeName = "vector"
            });
            await insertChunk.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return document;
    }

    public async Task<IReadOnlyList<StoredDocument>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT id, title, file_name, is_sample, uploaded_by, uploaded_at
            FROM documents
            ORDER BY uploaded_at DESC
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var documents = new List<StoredDocument>();
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new StoredDocument(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return documents;
    }

    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM documents WHERE id = @id");
        command.Parameters.AddWithValue("id", documentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Passage>> SearchAsync(float[] query, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
            return [];

        await using var command = _dataSource.CreateCommand(
            """
            SELECT c.id, c.document_id, d.title, d.file_name, d.is_sample, c.page_number, c.ordinal, c.content,
                   (1 - (c.embedding <=> @embedding))::real AS similarity
            FROM chunks AS c
            JOIN documents AS d ON d.id = c.document_id
            ORDER BY c.embedding <=> @embedding
            LIMIT @limit
            """);
        command.Parameters.AddWithValue("embedding", new Vector(query));
        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var passages = new List<Passage>();
        while (await reader.ReadAsync(cancellationToken))
        {
            int? page = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            passages.Add(new Passage(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                page,
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetFloat(8)));
        }

        return passages;
    }
}
