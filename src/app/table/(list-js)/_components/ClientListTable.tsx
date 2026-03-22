import { useEffect, useRef } from "react";
import type List from "list.js";

interface ClientListTableProps {
  id: string;
  options: {
    valueNames: string[];
    page: number;
    pagination: {
      innerWindow: number;
      outerWindow: number;
      paginationClass: string;
    };
  };
}

const ClientListTable = ({ id, options }: ClientListTableProps) => {
  const listRef = useRef<List | null>(null);

  useEffect(() => {
    // Only run on client side
    if (typeof window !== "undefined") {
      const initList = async () => {
        try {
          // Dynamically import list.js
          const List = (await import("list.js")).default;

          // Initialize the list
          if (!listRef.current) {
            listRef.current = new List(id, options);
          }

          // Cleanup function
          return () => {
            if (listRef.current) {
              // @ts-ignore - list.js doesn't have a proper destroy method in types
              listRef.current.listContainer?.remove();
              listRef.current = null;
            }
          };
        } catch (error) {
          return error;
        }
      };

      initList();
    }
  }, [id, options]);

  return null; // This component doesn't render anything visible
};

export default ClientListTable;
