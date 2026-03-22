"use client";

import React from "react";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Table2Columns } from "iconoir-react";
import EmployeeTable from "@/app/table/(list-js)/_components/EmployeeTable";
import ListTable from "@/app/table/(list-js)/_components/ListTable";
import TablesLists from "@/app/table/(list-js)/_components/TablesLists";

const ListJsPage = () => {
  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="List Table"
        title=" Table "
        path={["List table"]}
        Icon={Table2Columns}
      />
      <Row>
        <EmployeeTable />
        <ListTable />
        <TablesLists />
      </Row>
    </Container>
  );
};

export default ListJsPage;
