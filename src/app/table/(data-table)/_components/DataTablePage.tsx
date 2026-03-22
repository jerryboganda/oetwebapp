"use client";
import React from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Table2Columns } from "iconoir-react";
import DefaultDatatable from "@/app/table/(data-table)/_components/DefaultDatatable";
import BorderedDatatable from "@/app/table/(data-table)/_components/BorderedDatatable";
import ButtonsDatatable from "@/app/table/(data-table)/_components/ButtonsDatatable";
import CallbackDatatable from "@/app/table/(data-table)/_components/CallbackDatatable";

const DataTablePage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Data Table"
          title=" Table "
          path={["Data Table"]}
          Icon={Table2Columns}
        />
        <Row>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Default Datatable</h5>
                <p>
                  DataTables has most features enabled by default, so all you
                  need to do to use it with your own tables is to call the
                  construction function: <code>$().DataTable();</code>.{" "}
                </p>
              </CardHeader>

              <CardBody className="p-0">
                <DefaultDatatable />
              </CardBody>
            </Card>
          </Col>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Row Border Bottom Example</h5>
                <p>
                  DataTables has most features enabled by default, so all you
                  need to do to use it with your own ables is to call the
                  construction function: <code>$().DataTable();</code> and
                  border bottom
                </p>
              </CardHeader>

              <CardBody className="p-0">
                <BorderedDatatable />
              </CardBody>
            </Card>
          </Col>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Buttons Example</h5>
                <p>
                  The Buttons extension for DataTables provides a common set of
                  options, API methods and styling to display buttons on a page
                  that will interact with a DataTable. The core library provides
                  the based framework upon which plug-ins can be built.
                </p>
              </CardHeader>

              <CardBody className="p-0">
                <ButtonsDatatable />
              </CardBody>
            </Card>
          </Col>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Row Created Callback Example</h5>
                <p>
                  The Buttons extension for DataTables provides a common set of
                  options, API methods and styling to display buttons on a page
                  that will interact with a DataTable. The core library provides
                  the based framework upon which plug-ins can be built.
                </p>
              </CardHeader>

              <CardBody className="p-0">
                <CallbackDatatable />
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default DataTablePage;
