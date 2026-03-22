"use client";
import React, { useRef, useEffect, useState } from "react";
import { Container, Row, Col } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Muuri from "muuri";
import { IconStack2 } from "@tabler/icons-react";

interface Task {
  id: string;
  title: string;
  dueDate: string;
  comments: number;
  progress: string;
  image?: string;
}

interface Column {
  id: string;
  title: string;
  icon: string;
  items: Task[];
}

const KanbanBoard: React.FC = () => {
  const boardRef = useRef<HTMLDivElement>(null);
  const columnGridsRef = useRef<Muuri[]>([]);
  const boardGridRef = useRef<any>(null);
  const [isMounted, setIsMounted] = useState(false);

  const columns: Column[] = [
    {
      id: "1",
      title: "To Do",
      icon: "ph-list-bullets",
      items: [
        {
          id: "1",
          title: "Create homepage wireframe.",
          dueDate: "Nov 22",
          comments: 2,
          progress: "1/2",
        },
        {
          id: "2",
          title: "Draft new article content.",
          dueDate: "Dec 19",
          comments: 2,
          progress: "1/2",
          image: "/images/profile/07.jpg",
        },
        {
          id: "3",
          title: "Analyze client comments.",
          dueDate: "Sep 28",
          comments: 2,
          progress: "1/2",
        },
      ],
    },
    {
      id: "2",
      title: "IN PROGRESS",
      icon: "ph-chart-line-up",
      items: [
        {
          id: "4",
          title: "Prepare email marketing.",
          dueDate: "Jul 10",
          comments: 2,
          progress: "1/2",
          image: "/images/profile/10.jpg",
        },
      ],
    },
    {
      id: "3",
      title: "REVIEW",
      icon: "ph-eye",
      items: [
        {
          id: "5",
          title: "Revise product listings.",
          dueDate: "Mar 27",
          comments: 2,
          progress: "1/2",
        },
        {
          id: "6",
          title: "Create initial app mockup.",
          dueDate: "Apr 09",
          comments: 2,
          progress: "1/2",
        },
      ],
    },
    {
      id: "4",
      title: "DONE",
      icon: "ph-check-square-offset",
      items: [
        {
          id: "7",
          title: "Compile financial data.",
          dueDate: "Jul 24",
          comments: 2,
          progress: "1/2",
          image: "/images/profile/05.jpg",
        },
      ],
    },
    {
      id: "5",
      title: "TESTED",
      icon: "ph-check-circle",
      items: [
        {
          id: "8",
          title: "Gather market insights.",
          dueDate: "Oct 04",
          comments: 2,
          progress: "1/2",
          image: "/images/profile/09.jpg",
        },
        {
          id: "9",
          title: "Improve page load times.",
          dueDate: "Aug 23",
          comments: 2,
          progress: "1/2",
          image: undefined,
        },
      ],
    },
  ];

  useEffect(() => {
    setIsMounted(true);
  }, []);

  useEffect(() => {
    if (!isMounted || !boardRef.current) return;

    let isInitialized = false;
    const initKanban = async () => {
      const Muuri = (await import("muuri")).default;

      if (isInitialized) return;
      isInitialized = true;

      const itemContainers = boardRef.current!.querySelectorAll(
        ".board-column-content"
      );
      const columnGrids: Muuri[] = [];

      itemContainers.forEach((container) => {
        const grid = new Muuri(container as HTMLElement, {
          items: ".board-item",
          layoutDuration: 400,
          dragEnabled: true,
          dragSort: () => columnGrids,
          dragContainer: document.body,
          dragRelease: { duration: 400, easing: "ease" },
        })
          .on("dragStart", (item) => {
            const el = item.getElement();
            if (el) {
              el.style.width = `${item.getWidth()}px`;
              el.style.height = `${item.getHeight()}px`;
            }
          })
          .on("dragReleaseEnd", (item) => {
            const el = item.getElement();
            if (el) {
              el.style.width = "";
              el.style.height = "";
            }
            columnGrids.forEach((grid) => grid.refreshItems());
          })
          .on("layoutStart", () => {
            boardGridRef.current?.refreshItems().layout();
          });

        columnGrids.push(grid);
      });

      const boardElement = boardRef.current!.querySelector(".board");
      if (boardElement) {
        boardGridRef.current = new Muuri(boardElement as HTMLElement, {
          layout: { horizontal: true },
          dragEnabled: true,
          dragContainer: document.body,
          dragRelease: { duration: 400, easing: "ease" },
          dragStartPredicate: (_item, event) => {
            const target = event.target as HTMLElement;
            return target.closest(".board-column-header") !== null;
          },
        });
      }

      columnGridsRef.current = columnGrids;
    };

    initKanban();

    return () => {
      columnGridsRef.current.forEach((grid) => grid.destroy());
      if (boardGridRef.current) {
        boardGridRef.current.destroy();
      }
    };
  }, [isMounted]);

  if (!isMounted) {
    return (
      <Container fluid>
        <Breadcrumbs
          mainTitle="Kanban Board"
          title="Apps"
          path={["Kanban Board"]}
          Icon={IconStack2}
        />
      </Container>
    );
  }

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Kanban Board"
        title="Apps"
        path={["Kanban Board"]}
        Icon={IconStack2}
      />
      <Row>
        <Col sm="12">
          <div className="kanban-board-container app-scroll" ref={boardRef}>
            <div className="board">
              {columns.map((column) => (
                <div key={column.id} className="board-column app-scroll">
                  <div className="board-column-header">
                    <i className={`ph-bold ${column.icon} me-2 f-s-16`} />
                    {column.title}
                  </div>
                  <div className="board-column-content-wrapper">
                    <div className="board-column-content">
                      {column.items.map((task) => (
                        <div
                          key={task.id}
                          className="board-item cursor-move bg-white rounded-lg p-4 mb-4 shadow-sm transition-all duration-200 ease-in-out"
                        >
                          <div
                            className={`board-item-content ${task.image ? "p-0" : ""}`}
                          >
                            {task.image && (
                              <div className="board-images">
                                <img
                                  alt=""
                                  className="img-fluid"
                                  src={task.image}
                                />
                              </div>
                            )}
                            <div className={task.image ? "p-3" : ""}>
                              <h6 className="mb-0">{task.title}</h6>
                              <div className="board-footer">
                                <span className="badge bg-light-danger f-s-14">
                                  <i className="ph-bold ph-clock-afternoon" />
                                  {task.dueDate}
                                </span>
                                <i className="ph-bold ph-list f-s-14 me-2" />
                                <span className="f-s-14 me-2">
                                  <i className="ph-bold ph-chat-text" />
                                  <span>{task.comments}</span>
                                </span>
                                <span className="badge bg-light-primary f-s-14">
                                  <i className="ph-bold ph-check-square-offset" />
                                  {task.progress}
                                </span>
                              </div>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export default KanbanBoard;
