import React, { useEffect, useRef } from "react";
import { Card, CardBody, CardHeader, Col } from "reactstrap";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faUser } from "@fortawesome/free-solid-svg-icons";
import { IconMail } from "@tabler/icons-react";

const TablesLists: React.FC = () => {
  const sideListRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const listRef1 = useRef<HTMLDivElement>(null);
  const paginationRef = useRef<HTMLUListElement>(null);

  useEffect(() => {
    const setupLists = async () => {
      if (typeof window !== "undefined") {
        const ListModule = await import("list.js"); // <-- only imported in browser
        const List = ListModule.default;

        if (sideListRef.current) {
          new List(sideListRef.current, { valueNames: ["side"] });
        }
        if (listRef.current) {
          new List(listRef.current, { valueNames: ["name"] });
        }
        if (listRef1.current && paginationRef.current) {
          new List(listRef1.current, {
            valueNames: ["name"],
            page: 3,
            pagination: {
              innerWindow: 1,
              outerWindow: 1,
              paginationClass: "pagination",
            },
          });
        }
      }
    };

    setupLists();
  }, []);

  const sideListData = [
    {
      id: 1,
      name: "Olive Yew",
      description: "This is the content of the email.",
      time: "28 min",
      theme: "info",
    },
    {
      id: 2,
      name: "Bea Mine",
      description: "It enables users to easily.",
      time: "48 min",
      theme: "success",
    },
    {
      id: 3,
      name: "Toi Story",
      description: "Companies can use to convey.",
      time: "2 hours",
      theme: "primary",
    },
    {
      id: 4,
      name: "Art Decco",
      description: "System Software is closer.",
      time: "1 day",
      theme: "info",
    },
  ];

  const searchListData = [
    "Guybrush Threepwood",
    "Elaine Marley",
    "LeChuck",
    "Stan",
    "Voodoo Lady",
    "Herman Toothrot",
    "Meathook",
    "Carla",
    "Otis",
    "Rapp Scallion",
    "Rum Rogers Sr.",
    "Men of Low Moral Fiber",
    "Murray",
    "Cannibals",
  ];

  const tableData = ["Jonny Stromberg", "Jonas Arnklint", "Martina Elm"];

  return (
    <>
      <Col md="6" xxl="4">
        <Card>
          <CardHeader>
            <h5>Existing list</h5>
          </CardHeader>
          <CardBody>
            <div id="sidelist" ref={sideListRef}>
              <div className="row mb-3">
                <div className="col-md">
                  <input
                    type="search"
                    className="form-control search b-r-22"
                    placeholder="Search..."
                  />
                </div>
                <div className="col-md-auto">
                  <button
                    className="sort btn btn-sm btn-secondary b-r-22"
                    data-sort="side"
                  >
                    Sort by name
                  </button>
                </div>
              </div>
              <div className="list existing-list">
                {sideListData.map(({ id, name, description, time, theme }) => (
                  <div
                    key={id}
                    data-id={id}
                    className="d-flex justify-content-between"
                  >
                    <div>
                      <span
                        className={`position-relative h-40 w-40 d-flex-center b-r-50 text-light-${theme}`}
                      >
                        <FontAwesomeIcon icon={faUser} />
                        <span className="position-absolute end-0 top-0 p-1 bg-light border border-light rounded-circle"></span>
                      </span>
                    </div>
                    <div className="flex-grow-1 ps-2">
                      <h6 className="link side">{name}</h6>
                      <p>{description}</p>
                    </div>
                    <div className="text-muted">{time}</div>
                  </div>
                ))}
              </div>
            </div>
          </CardBody>
        </Card>
      </Col>

      <Col md="6" xxl="4">
        <Card>
          <CardHeader>
            <h5>Search List Table</h5>
          </CardHeader>
          <CardBody>
            <div id="test-list" ref={listRef}>
              <div className="mb-3">
                <input
                  type="search"
                  className="fuzzy-search form-control search b-r-22"
                  placeholder="Search..."
                />
              </div>
              <ul className="list fuzzy-list">
                {searchListData.map((name, idx) => (
                  <li key={idx}>
                    <p className="name">{name}</p>
                  </li>
                ))}
              </ul>
            </div>
          </CardBody>
        </Card>
      </Col>

      <Col md="6" xxl="4">
        <Card>
          <CardHeader>
            <h5>Table with Pagination</h5>
          </CardHeader>
          <CardBody>
            <div id="user" ref={listRef1}>
              <div className="mb-3">
                <input
                  type="search"
                  className="form-control search b-r-22"
                  placeholder="Search..."
                />
              </div>
              <table className="table table-bordered align-middle">
                <tbody className="list">
                  {tableData.map((name, idx) => (
                    <tr key={idx}>
                      <td className="name">{name}</td>
                      <td>
                        <button
                          type="button"
                          className="btn btn-light-secondary float-end b-r-22"
                        >
                          <IconMail size={18} /> Message
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <div className="list-pagination">
                <ul className="pagination" ref={paginationRef}></ul>
              </div>
            </div>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default TablesLists;
