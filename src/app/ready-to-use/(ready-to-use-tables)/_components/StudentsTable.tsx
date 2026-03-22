import React from "react";
import { Card } from "reactstrap";
import { studentsData } from "@/Data/Table/DataTable/studentsData";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

const StudentsTable = () => {
  const columns = [
    {
      key: "select",
      header: (
        <input
          className="form-check-input mt-0"
          type="checkbox"
          id="select-all"
        />
      ),
      render: () => <input className="form-check-input mt-0" type="checkbox" />,
    },
    { key: "name", header: "Name" },
    { key: "parentName", header: "Parent Name" },
    {
      key: "id",
      header: "ID",
      className: "f-w-500",
    },
    {
      key: "contact",
      header: "Contact",
      render: (value: any) => <span className="text-success">{value}</span>,
    },
    { key: "city", header: "City" },
    { key: "date", header: "Date" },
    {
      key: "grade",
      header: "Grade",
      render: (value: string) => (
        <span
          className={`badge text-light-${value === "A" ? "success" : "warning"}`}
        >
          {value}
        </span>
      ),
    },
  ];

  // Inject a unique key (using index since ID might not be unique in dummy data)
  const dataWithIds = studentsData.map((student, index) => ({
    ...student,
    _id: `${student.id}-${index}`,
    rowClassName: index % 2 === 0 ? "students-table-odd" : "",
  }));

  return (
    <Card>
      <CustomDataTable
        title="Students List"
        description=""
        columns={columns}
        data={dataWithIds}
        rowKey="_id"
        showActions
        onEdit={(student) => {
          console.log("Edit student:", student);
        }}
        onDelete={(student) => {
          console.log("Delete student:", student);
        }}
      />
    </Card>
  );
};

export default StudentsTable;
