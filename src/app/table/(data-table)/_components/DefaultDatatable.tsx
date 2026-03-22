import React from "react";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";
import { users } from "@/Data/Table/DataTable/defaultDatatable";
import { IconEdit, IconTrash } from "@tabler/icons-react";

const DefaultDatatable = () => {
  const columns = [
    { key: "name", header: "Name" },
    {
      key: "position",
      header: "Position",
      render: (value: string) => (
        <span className="badge text-light-primary">{value}</span>
      ),
    },
    { key: "location", header: "Office" },
    { key: "age", header: "Age" },
    { key: "salary", header: "Start date" },
    { key: "totalSalary", header: "Salary" },
    {
      key: "action",
      header: "Action",
      render: () => (
        <div className="d-flex gap-1">
          <button
            type="button"
            className="btn btn-light-success icon-btn b-r-4"
          >
            <IconEdit size={18} className="text-success" />
          </button>
          <button
            type="button"
            className="btn btn-light-danger icon-btn b-r-4 delete-btn"
          >
            <IconTrash size={18} className="text-danger" />
          </button>
        </div>
      ),
    },
  ];

  // Add unique key for each row to avoid React warnings
  const dataWithKeys = users.map((user, index) => ({
    ...user,
    _key: `${user.name}-${index}`,
  }));

  return (
    <div className="app-scroll table-responsive app-datatable-default">
      <CustomDataTable
        title=""
        description=""
        columns={columns}
        data={dataWithKeys}
        rowKey="_key"
        showActions={false}
      />
    </div>
  );
};

export default DefaultDatatable;
