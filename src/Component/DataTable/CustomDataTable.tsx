import React, { useEffect, useRef } from "react";
import { Button, Card, Table } from "react-bootstrap";
import { IconEdit, IconTrash } from "@tabler/icons-react";
import "datatables.net-dt/css/dataTables.dataTables.min.css";

interface Column<T = any> {
  key: string;
  header: string | React.ReactNode;
  render?: (data: any, item: T) => React.ReactNode;
  className?: string;
}

interface DataTableProps {
  rowKey?: string;
  title?: string;
  description?: string | React.ReactNode;
  showTitle?: boolean;
  showDescription?: boolean;
  columns: Column[];
  data: any[];
  showActions?: boolean;
  onEdit?: (item: any) => void;
  onDelete?: (item: any) => void;
  className?: string;
  cardClassName?: string;
  tableClassName?: string;
  pageLength?: number;
  showLengthMenu?: boolean;
  showFooter?: boolean;
  footerData?: any[];
  footerColumns?: Column[];
}

const CustomDataTable: React.FC<DataTableProps> = ({
  rowKey = "id",
  title = "Default Datatable",
  description = "DataTables has most features enabled by default",
  showTitle = true,
  showDescription = true,
  columns,
  data,
  showActions = true,
  onEdit,
  onDelete,
  className = "",
  cardClassName = "",
  tableClassName = "w-100 align-middle mb-0",
  pageLength = 10,
  showLengthMenu = true,
  showFooter = false,
  footerData = [],
  footerColumns = [],
}) => {
  const tableRef = useRef<HTMLTableElement>(null);
  const dataTableRef = useRef<any>(null);

  useEffect(() => {
    if (typeof window !== "undefined") {
      const initDataTable = async () => {
        const DataTable = (await import("datatables.net-dt")).default;

        if (tableRef.current && !dataTableRef.current) {
          dataTableRef.current = new DataTable(tableRef.current, {
            dom: showLengthMenu
              ? `<"dt-layout-top"<"dt-layout-row"<"dt-layout-cell dt-layout-start"l><"dt-layout-cell dt-layout-end"f>>>
                 <"dt-layout-middle"tr>
                 <"dt-layout-bottom"<"dt-layout-row"<"dt-layout-cell dt-layout-start"i><"dt-layout-cell dt-layout-end"p>>>`
              : `<"dt-layout-top"<"dt-layout-row"<"dt-layout-cell dt-layout-end"f>>>
                 <"dt-layout-middle"tr>
                 <"dt-layout-bottom"<"dt-layout-row"<"dt-layout-cell dt-layout-start"i><"dt-layout-cell dt-layout-end"p>>>`,
            pagingType: "full_numbers",
            pageLength: pageLength,
            language: {
              search: "_INPUT_",
              searchPlaceholder: "Search...",
              lengthMenu: "Show _MENU_ entries",
              info: "Showing _START_ to _END_ of _TOTAL_ entries",
              infoEmpty: "Showing 0 to 0 of 0 entries",
              infoFiltered: "(filtered from _MAX_ total entries)",
            },
          });
        }
      };

      initDataTable();
    }
    return undefined;
  }, [data, pageLength, showLengthMenu]);

  return (
    <div className={className}>
      <Card className={cardClassName}>
        {(showTitle || showDescription) && (
          <Card.Header>
            {showTitle && <h5>{title}</h5>}
            {showDescription && description && <p>{description}</p>}
          </Card.Header>
        )}

        <Card.Body className="p-0">
          <div
            className={`app-scroll table-responsive ${className} app-datatable-default cursor-pointer`}
          >
            <Table ref={tableRef} striped hover className={tableClassName}>
              <thead>
                <tr>
                  {columns.map((column) => (
                    <th key={column.key} className={column.className}>
                      {column.header}
                    </th>
                  ))}
                  {showActions && <th>Action</th>}
                </tr>
              </thead>
              <tbody>
                {data.map((item) => (
                  <tr key={item[rowKey]}>
                    {columns.map((column) => (
                      <td key={`${item[rowKey]}-${column.key}`}>
                        {column.render
                          ? column.render(item[column.key], item)
                          : item[column.key]}
                      </td>
                    ))}
                    {showActions && (
                      <td>
                        {onEdit && (
                          <Button
                            variant="light-success"
                            className="me-2 p-1"
                            size="sm"
                            onClick={() => onEdit(item)}
                          >
                            <IconEdit size={18} className="text-success" />
                          </Button>
                        )}
                        {onDelete && (
                          <Button
                            variant="light-danger"
                            className="p-1 delete-btn"
                            size="sm"
                            onClick={() => onDelete(item)}
                          >
                            <IconTrash size={18} className="text-danger" />
                          </Button>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
              {showFooter && (
                <tfoot>
                  <tr>
                    {footerColumns.length > 0
                      ? footerColumns.map((column) => (
                          <th
                            key={`footer-${column.key}`}
                            className={column.className}
                          >
                            {column.header}
                          </th>
                        ))
                      : columns.map((column) => (
                          <th
                            key={`footer-${column.key}`}
                            className={column.className}
                          >
                            {column.header}
                          </th>
                        ))}
                    {showActions && <th>Action</th>}
                  </tr>
                  {footerData.map((item, index) => (
                    <tr key={`footer-row-${index}`}>
                      {footerColumns.length > 0
                        ? footerColumns.map((column) => (
                            <td key={`footer-${column.key}-${index}`}>
                              {column.render
                                ? column.render(item[column.key], item)
                                : item[column.key]}
                            </td>
                          ))
                        : columns.map((column) => (
                            <td key={`footer-${column.key}-${index}`}>
                              {column.render
                                ? column.render(item[column.key], item)
                                : item[column.key]}
                            </td>
                          ))}
                      {showActions && <td></td>}
                    </tr>
                  ))}
                </tfoot>
              )}
            </Table>
          </div>
        </Card.Body>
      </Card>
    </div>
  );
};

export default CustomDataTable;
