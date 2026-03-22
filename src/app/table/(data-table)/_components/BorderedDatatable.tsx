import React from "react";
import { employeesData } from "@/Data/Table/DataTable/borderedDatatable";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

type Employee = (typeof employeesData)[0];

const BorderedDatatable = () => {
  const columns = [
    {
      key: "name",
      header: "Name",
      render: (_: any, row: Employee) => (
        <div className="d-flex align-items-center">
          <div className="h-30 w-30 d-flex-center b-r-50 overflow-hidden text-bg-info">
            <img src={row.imageUrl} alt={row.name} className="img-fluid" />
          </div>
          <p className="mb-0 ps-2">{row.name}</p>
        </div>
      ),
    },
    { key: "position", header: "Position" },
    { key: "location", header: "Office" },
    { key: "age", header: "Age" },
    { key: "salary", header: "Start date" },
    { key: "totalSalary", header: "Salary" },
  ];

  const employeesWithId = employeesData.map((item, index) => ({
    ...item,
    _key: `${item.name}-${index}`,
  }));

  return (
    <div>
      <CustomDataTable
        title=""
        description=""
        columns={columns}
        data={employeesWithId}
        rowKey="_key"
        showActions={false}
      />
    </div>
  );
};

export default BorderedDatatable;
