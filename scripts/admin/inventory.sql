-- Fixed inventory (MockBundles uses MockType not BundleType; integer Status mapped)
\pset border 2
\pset format aligned

SELECT 'ContentPapers' AS tbl, COALESCE("SubtestCode",'?') AS sub,
  CASE "Status" WHEN 0 THEN '0-Draft' WHEN 1 THEN '1-InReview' WHEN 2 THEN '2-EditorReview' WHEN 3 THEN '3-PublisherApproval' WHEN 4 THEN '4-Published' WHEN 5 THEN '5-Rejected' WHEN 6 THEN '6-Archived' ELSE 'enum-' || "Status"::text END AS status,
  COUNT(*)::int AS n
FROM "ContentPapers" GROUP BY "SubtestCode","Status"
UNION ALL
SELECT 'MockBundles', COALESCE("MockType",'?'),
  CASE "Status" WHEN 0 THEN '0-Draft' WHEN 1 THEN '1-InReview' WHEN 2 THEN '2-EditorReview' WHEN 3 THEN '3-PublisherApproval' WHEN 4 THEN '4-Published' WHEN 5 THEN '5-Rejected' WHEN 6 THEN '6-Archived' ELSE 'enum-' || "Status"::text END,
  COUNT(*)::int
FROM "MockBundles" GROUP BY "MockType","Status"
UNION ALL
SELECT 'SpeakingMockSets','speaking', "Status"::text, COUNT(*)::int FROM "SpeakingMockSets" GROUP BY "Status"
UNION ALL
SELECT 'ConversationTemplates','-', COALESCE("Status",'null'), COUNT(*)::int FROM "ConversationTemplates" GROUP BY "Status"
UNION ALL
SELECT 'GrammarLessons','-', COALESCE("Status",'null'), COUNT(*)::int FROM "GrammarLessons" GROUP BY "Status"
UNION ALL
SELECT 'PronunciationDrills','-', COALESCE("Status",'null'), COUNT(*)::int FROM "PronunciationDrills" GROUP BY "Status"
UNION ALL
SELECT 'VocabularyTerms','-', COALESCE("Status",'null'), COUNT(*)::int FROM "VocabularyTerms" GROUP BY "Status"
UNION ALL
SELECT 'StrategyGuides','-', COALESCE("Status",'null'), COUNT(*)::int FROM "StrategyGuides" GROUP BY "Status"
UNION ALL
SELECT 'RulebookVersions', COALESCE("Kind",'?'), COALESCE("Status",'null'), COUNT(*)::int FROM "RulebookVersions" GROUP BY "Kind","Status"
UNION ALL
SELECT 'SpeakingCalibrationSamples','-', COALESCE("Status"::text,'null'), COUNT(*)::int FROM "SpeakingCalibrationSamples" GROUP BY "Status"
ORDER BY 1,2,3;
