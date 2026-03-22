import React, { useEffect, useRef, useState } from "react";
import Sortable from "sortablejs";

interface NestedItem {
  id: string | number;
  title: string;
  children?: NestedItem[];
  variant?: string;
}

interface Props {
  items: NestedItem[];
  variantMode?: boolean;
}

const NestedSortableDataList: React.FC<Props> = ({
  items,
  variantMode = false,
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const [_sortedIds, setSortedIds] = useState<string[]>([]);

  useEffect(() => {
    if (typeof window === "undefined") return;

    containerRef.current?.querySelectorAll(".nested-sortable").forEach((el) => {
      new Sortable(el as HTMLElement, {
        group: "nested",
        animation: 150,
        fallbackOnBody: true,
        swapThreshold: 0.65,
        onSort: (e) => {
          const result = Array.from(e.to.children)
            .map((item) => (item as HTMLElement).getAttribute("data-id") ?? "")
            .filter((id) => id !== "");
          setSortedIds(result);
        },
      });
    });
  }, []);

  const renderList = (list: NestedItem[]) => (
    <div className="list-group nested-sortable">
      {list.map((item) => {
        const hasChildren = item.children && item.children.length > 0;
        const variantClass = variantMode
          ? `bg-light-${item.variant || "primary"}`
          : "";
        return (
          <div
            key={item.id}
            data-id={item.id}
            className={`list-group-item ${variantClass}`}
          >
            {variantMode && (
              <i
                className={`ph-bold ${
                  hasChildren ? "ph-plus me-3" : "ph-minus me-3"
                }`}
              />
            )}
            {item.title}
            {hasChildren && renderList(item.children!)}
          </div>
        );
      })}
    </div>
  );

  return <div ref={containerRef}>{renderList(items)}</div>;
};

// Predefined variants for SSR compatibility
const PREDEFINED_VARIANTS = [
  "primary",
  "success",
  "secondary",
  "danger",
  "info",
  "warning",
  "dark",
];

// Deterministic variant assignment based on ID
function getStableVariant(id: string | number) {
  const index =
    typeof id === "string"
      ? id.charCodeAt(0) % PREDEFINED_VARIANTS.length
      : id % PREDEFINED_VARIANTS.length;
  return PREDEFINED_VARIANTS[index];
}

const nestedListData: NestedItem[] = [
  { id: 1, title: "Tital 1" },
  { id: 2, title: "Tital 2" },
  {
    id: 3,
    title: "Tital3",
    children: [
      { id: 4, title: "Tital 4" },
      { id: 5, title: "Tital 5" },
      { id: 6, title: "Tital 6" },
    ],
  },
  { id: 7, title: "Tital 7" },
  { id: 8, title: "Tital 8" },
  {
    id: 10,
    title: "Tital10",
    children: [
      { id: 11, title: "Tital 11" },
      { id: 12, title: "Tital 12" },
    ],
  },
];

export default function NestedSortableList() {
  const [variantItems, setVariantItems] = useState<NestedItem[]>([]);

  useEffect(() => {
    setVariantItems(
      nestedListData.map((item) => ({
        ...item,
        variant: getStableVariant(item.id) || "",
      }))
    );
  }, []);

  return (
    <div className="row">
      <div className="col-xxl-6">
        <div className="card">
          <div className="card-header">
            <h5>Nestable List</h5>
          </div>
          <div className="card-body">
            <NestedSortableDataList items={nestedListData} />
          </div>
        </div>
      </div>

      <div className="col-xxl-6">
        <div className="card">
          <div className="card-header">
            <h5>Colour Variant Nestable List</h5>
          </div>
          <div className="card-body">
            {variantItems.length > 0 ? (
              <NestedSortableDataList items={variantItems} variantMode />
            ) : (
              <div className="list-group">
                {nestedListData.map((item) => (
                  <div
                    key={item.id}
                    className="list-group-item bg-light-secondary"
                  >
                    {item.title}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
