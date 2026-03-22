import React from "react";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";
import { callbackTableData } from "@/Data/Table/DataTable/callbackDatatable";

const CallbackDatatable = () => {
  const columns = [
    { key: "name", header: "Name" },
    { key: "position", header: "Position" },
    { key: "location", header: "Office" },
    { key: "age", header: "Age" },
    { key: "salary", header: "Start date" },
    { key: "totalSalary", header: "Salary" },
  ];

  const dataWithKeys = callbackTableData.map((item, index) => ({
    ...item,
    _key: `${item.name}-${index}`,
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

export default CallbackDatatable;
