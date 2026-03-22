export interface KanbanItem {
  id: string | number;
  title: string;
  description: string;
  // Add any other properties that your kanban items have
}

export interface KanbanColumn {
  id: string | number;
  title: string;
  items: KanbanItem[];
  // Add any other properties that your columns have
}

export interface KanbanData {
  columns: KanbanColumn[];
  // Add any other properties that your kanban data has
}
