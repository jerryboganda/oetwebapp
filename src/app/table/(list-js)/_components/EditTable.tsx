"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col } from "reactstrap";
import { IconSearch } from "@tabler/icons-react";
import ClientListTable from "./ClientListTable";

const EditTable = () => {
  const [isClient, setIsClient] = useState(false);
  const [isModalOpen, setIsModalOpen] = useState(false);

  useEffect(() => {
    setIsClient(true);
  }, []);

  const listOptions = {
    valueNames: ["id", "employee", "email", "contact", "date", "status"],
    page: 4,
    pagination: {
      innerWindow: 1,
      outerWindow: 1,
      paginationClass: "pagination",
    },
  };

  return (
    <>
      <Col lg={8}>
        <Card>
          <CardHeader>
            <h5>Add, Edit & Remove table</h5>
          </CardHeader>
          <CardBody className="p-0">
            {isClient && <ClientListTable id="myTable" options={listOptions} />}
            <div id="myTable">
              <div className="list-table-header d-flex justify-content-sm-between mb-3">
                <button
                  type="button"
                  className="btn btn-primary b-r-22"
                  onClick={() => setIsModalOpen(true)}
                >
                  Add
                </button>
                <div
                  className={`modal fade ${isModalOpen ? "show d-block" : ""}`}
                  id="exampleModal"
                  tabIndex={-1}
                  onClick={() => setIsModalOpen(false)}
                >
                  <form id="add_employee_form">
                    <div className="modal-dialog">
                      <div className="modal-content">
                        <div className="modal-header">
                          <h1
                            className="modal-title fs-5"
                            id="exampleModalLabel"
                          >
                            Add Employee
                          </h1>
                          <button
                            type="button"
                            className="btn-close m-0"
                            onClick={() => setIsModalOpen(false)}
                            aria-label="Close"
                          ></button>
                        </div>
                        <div className="modal-body">
                          <div className="employee mb-3">
                            <input type="hidden" id="id-field" />
                            <label className="form-label">Employee :</label>
                            <input
                              className="form-control"
                              type="text"
                              id="employee-field"
                              placeholder="employee"
                              required
                            />
                          </div>

                          <div className="email mb-3">
                            <label className="form-label">Email :</label>
                            <input
                              className="form-control"
                              type="email"
                              id="email-field"
                              placeholder="email"
                              required
                            />
                          </div>

                          <div className="contact mb-3">
                            <label className="form-label">contact :</label>
                            <input
                              className="form-control"
                              type="text"
                              id="contact-field"
                              placeholder="contact"
                              required
                            />
                          </div>

                          <div className="date mb-3">
                            <label className="form-label">date :</label>
                            <input
                              className="form-control"
                              type="date"
                              id="date-field"
                              required
                            />
                          </div>

                          <div className="status mb-3">
                            <label className="form-label">status :</label>
                            <select
                              className="form-select"
                              id="status-field"
                              aria-label="Default select example"
                            >
                              <option value="">Status</option>
                              <option value="success">Active</option>
                              <option value="danger">Block</option>
                            </select>
                          </div>
                        </div>
                        <div className="modal-footer add">
                          <input
                            type="button"
                            className="btn btn-secondary"
                            onClick={() => setIsModalOpen(false)}
                            value="Close"
                          />
                          <input
                            type="submit"
                            className="btn btn-primary"
                            id="add-btn"
                            value="Add"
                          />
                          <button
                            className="btn btn-success b-r-22"
                            id="edit-btn"
                          >
                            Edit
                          </button>
                        </div>
                      </div>
                    </div>
                  </form>
                </div>

                <form className="app-form app-icon-form " action="#">
                  <div className="position-relative ">
                    <input
                      type="search"
                      className="form-control search"
                      placeholder="Search..."
                      aria-label="Search"
                    />
                    <IconSearch size={18} className="text-dark" />
                  </div>
                </form>
              </div>

              <div className="overflow-auto app-scroll">
                <table className="table table-bottom-border  list-table-data align-middle mb-0">
                  <thead>
                    <tr className="app-sort">
                      <th className="d-none">ID</th>
                      <th className="sort" data-sort="employee" scope="col">
                        Employee
                      </th>
                      <th className="sort" data-sort="email" scope="col">
                        Email
                      </th>
                      <th className="sort" data-sort="contact" scope="col">
                        contact
                      </th>
                      <th className="sort" data-sort="date" scope="col">
                        Joining Date
                      </th>
                      <th className="sort" data-sort="status" scope="col">
                        Status
                      </th>
                      <th className="sort" data-sort="action" scope="col">
                        Edit
                      </th>
                      <th className="sort" data-sort="action" scope="col">
                        Delete
                      </th>
                    </tr>
                  </thead>
                  <tbody className="list" id="t-data">
                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                        />
                      </th>
                      <td className="id d-none">1</td>
                      <td className="employee">Allie Grater</td>
                      <td className="email">graterallie@gmail.com</td>
                      <td className="contact">8054478398</td>
                      <td className="date">2021-03-19</td>
                      <td className="status">
                        <span className="badge bg-danger-subtle text-danger text-uppercase">
                          Block
                        </span>
                      </td>
                      <td className="edit">
                        <button
                          className="btn edit-item-btn btn-sm btn-success b-r-22"
                          onClick={() => setIsModalOpen(true)}
                        >
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>

                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                          value="2"
                        />
                      </th>
                      <td className="id d-none">2</td>
                      <td className="employee">Rhoda Report</td>
                      <td className="email">reportrhoda@gmail.com</td>
                      <td className="contact">7765392112</td>
                      <td className="date">2020-01-19</td>
                      <td className="status">
                        <span className="badge bg-success-subtle text-success text-uppercase">
                          Active
                        </span>
                      </td>
                      <td className="edit">
                        <button className="btn edit-item-btn btn-sm btn-success b-r-22">
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>

                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                          value="3"
                        />
                      </th>
                      <td className="id d-none">3</td>
                      <td className="employee">Rose Bush</td>
                      <td className="email">rose@gmail.com</td>
                      <td className="contact">9674903425</td>
                      <td className="date">2020-10-26</td>
                      <td className="status">
                        <span className="badge bg-success-subtle text-success text-uppercase">
                          Active
                        </span>
                      </td>
                      <td className="edit">
                        <button className="btn edit-item-btn btn-sm btn-success b-r-22">
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>

                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                          value="4"
                        />
                      </th>
                      <td className="id d-none">4</td>
                      <td className="employee">Dave Allippa</td>
                      <td className="email">dave@gmail.com</td>
                      <td className="contact">6490537289</td>
                      <td className="date">2020-06-19</td>
                      <td className="status">
                        <span className="badge bg-danger-subtle text-danger text-uppercase">
                          Block
                        </span>
                      </td>
                      <td className="edit">
                        <button className="btn edit-item-btn btn-sm btn-success b-r-22">
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>

                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                          value="5"
                        />
                      </th>
                      <td className="id d-none">5</td>
                      <td className="employee">Emma Grate</td>
                      <td className="email">emma@gmail.com</td>
                      <td className="contact">9123456031</td>
                      <td className="date">2022-03-20</td>
                      <td className="status">
                        <span className="badge bg-danger-subtle text-danger text-uppercase">
                          Block
                        </span>
                      </td>
                      <td className="edit">
                        <button className="btn edit-item-btn btn-sm btn-success b-r-22">
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>

                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                          value="6"
                        />
                      </th>
                      <td className="id d-none">6</td>
                      <td className="employee">Mal Nurrisht</td>
                      <td className="email">Nurrishtmal@gmail.com</td>
                      <td className="contact">6490267839</td>
                      <td className="date">2019-12-29</td>
                      <td className="status">
                        <span className="badge bg-success-subtle text-success text-uppercase">
                          Active
                        </span>
                      </td>
                      <td className="edit">
                        <button className="btn edit-item-btn btn-sm btn-success b-r-22">
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>

                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                          value="7"
                        />
                      </th>
                      <td className="id d-none">7</td>
                      <td className="employee">Bess Twishes</td>
                      <td className="email">twishes@gmail.com</td>
                      <td className="contact">8702643868</td>
                      <td className="date">2020-5-16</td>
                      <td className="status">
                        <span className="badge bg-danger-subtle text-danger text-uppercase">
                          Block
                        </span>
                      </td>
                      <td className="edit">
                        <button className="btn edit-item-btn btn-sm btn-success b-r-22">
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>

                    <tr>
                      <th scope="row">
                        <input
                          className="form-check-input mt-0 ms-2"
                          type="checkbox"
                          name="item"
                          value="8"
                        />
                      </th>
                      <td className="id d-none">8</td>
                      <td className="employee">C. Yasoon</td>
                      <td className="email">yasoon@gmail.com</td>
                      <td className="contact">9345067125</td>
                      <td className="date">2022-02-16</td>
                      <td className="status">
                        <span className="badge bg-success-subtle text-success text-uppercase">
                          Active
                        </span>
                      </td>
                      <td className="edit">
                        <button className="btn edit-item-btn btn-sm btn-success b-r-22">
                          Edit
                        </button>
                      </td>
                      <td className="remove">
                        <button className="btn remove-item-btn btn-sm btn-danger b-r-22">
                          Remove
                        </button>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div className="list-pagination">
                <ul className="pagination"></ul>
              </div>
            </div>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default EditTable;
