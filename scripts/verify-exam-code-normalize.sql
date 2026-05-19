SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 3;
SELECT
  (SELECT COUNT(*) FROM "Users" WHERE "ActiveExamTypeCode" = 'oet') AS users_lc,
  (SELECT COUNT(*) FROM "Users" WHERE "ActiveExamTypeCode" = 'OET') AS users_uc,
  (SELECT COUNT(*) FROM "VocabularyTerms" WHERE "ExamTypeCode" = 'oet') AS vocab_lc,
  (SELECT COUNT(*) FROM "VocabularyTerms" WHERE "ExamTypeCode" = 'OET') AS vocab_uc;
