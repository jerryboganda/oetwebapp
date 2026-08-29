-- W11 unique NormalizedWord index. Apply ONLY after merge-vocabulary-duplicates.sql
-- leaves zero duplicate clusters. CONCURRENTLY cannot run inside a transaction.

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "UX_VocabularyWords_NormalizedWord"
ON "VocabularyWords" ("NormalizedWord")
WHERE "NormalizedWord" <> '';
