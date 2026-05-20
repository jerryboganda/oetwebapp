DELETE FROM "ContentPaperAssets"
WHERE "Id" IN (
  '16b3de7c3c644204a4044e12afcc18c6',
  '836f513b7f8a4c58984114a580bf3ca0',
  'd90840da4202496fa6885a5c5d99e1a9',
  '6dc08830b57e40a2972249d6e1249b2e'
)
RETURNING "Id", "Part", "DisplayOrder";
